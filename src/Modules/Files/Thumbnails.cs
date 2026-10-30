using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DaisysServer.Modules.Files;

/// <summary>
/// Windows' own thumbnails (IShellItemImageFactory, as Explorer shows: photos, videos, PDFs, documents), made on a
/// thread of their own (STA, as thumbnail handlers expect), one at a time. Only real thumbnails: a file Windows only
/// has an icon for gets none (the client draws its own). Opaque ones go as JPEG, ones with see-through parts as PNG.
/// A handler stuck for 15 s is left behind on its thread, and a new thread takes over.
/// </summary>
internal static class Thumbnails
{
    public sealed record Thumb(byte[] Data, string Type, int Width, int Height);

    private static Worker worker = new();

    public static async Task<Thumb?> GetAsync(string path, int size)
    {
        var w = worker;
        if (w.Stuck)
        {
            Interlocked.CompareExchange(ref worker, new Worker(), w);
            w = worker;
        }
        var job = new TaskCompletionSource<Thumb?>(TaskCreationOptions.RunContinuationsAsynchronously);
        w.Add(() =>
        {
            try { job.TrySetResult(Make(path, size)); }
            catch (Exception ex) { job.TrySetException(ex); }
        });
        try { return await job.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (TimeoutException) { return null; }
    }

    private sealed class Worker
    {
        private readonly BlockingCollection<Action> queue = new();
        private long busySince;

        public Worker()
        {
            var t = new Thread(() =>
            {
                foreach (var a in queue.GetConsumingEnumerable())
                {
                    Interlocked.Exchange(ref busySince, Environment.TickCount64);
                    a();
                    Interlocked.Exchange(ref busySince, 0);
                }
            }) { IsBackground = true, Name = "Files thumbnails" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public bool Stuck => Interlocked.Read(ref busySince) is long since and > 0 && Environment.TickCount64 - since > 15000;

        public void Add(Action a) => queue.Add(a);
    }

    private static Thumb? Make(string path, int size)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
        IntPtr hbm = IntPtr.Zero;
        try
        {
            if (factory.GetImage(new Size { cx = size, cy = size }, ThumbnailOnly, out hbm) != 0 || hbm == IntPtr.Zero) return null;
            var bm = new BITMAP();
            if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            var info = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var px = new byte[w * h * 4];
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(dc, hbm, 0, (uint)h, px, ref info, 0) == 0) return null;
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }

            // see-through? (no alpha at all means opaque)
            bool allSolid = true, allZero = true;
            for (int i = 3; i < px.Length; i += 4)
            {
                if (px[i] != 255) allSolid = false;
                if (px[i] != 0) allZero = false;
            }
            bool alpha = !allSolid && !allZero;
            BitmapEncoder encoder = alpha ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 85 };
            var source = BitmapSource.Create(w, h, 96, 96, alpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, px, w * 4);
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return new Thumb(ms.ToArray(), alpha ? "png" : "jpeg", w, h);
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            Marshal.ReleaseComObject(factory);
        }
    }

    // ---- Windows

    private const int ThumbnailOnly = 0x08; // SIIGBF_THUMBNAILONLY: no icon instead

    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, ref BITMAP bm);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
