using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RenoDXMfgUnlocker;

/// <summary>Window presentation only: monitor work area and native maximize bounds.</summary>
internal static class DesktopWindowLayout
{
    internal static Size PreferredSize(Size workArea)=>new(Math.Max(1,Math.Min(1440,workArea.Width-24)),Math.Max(1,Math.Min(900,workArea.Height-24)));
    internal static Rect WorkArea(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==IntPtr.Zero)return SystemParameters.WorkArea;
        var info=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromWindow(handle,2),ref info))return SystemParameters.WorkArea;
        var matrix=HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
        return new Rect(matrix.Transform(new Point(info.Work.Left,info.Work.Top)),matrix.Transform(new Point(info.Work.Right,info.Work.Bottom)));
    }
    internal static void Attach(Window window)
    {
        window.SourceInitialized+=(_,_)=>{
            var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>{
                if(message!=0x24)return IntPtr.Zero; // WM_GETMINMAXINFO: all values here are physical pixels.
                var monitor=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};
                if(!GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor))return IntPtr.Zero;
                var bounds=Marshal.PtrToStructure<MinMaxInfo>(lParam);var dpi=VisualTreeHelper.GetDpi(window);
                bounds.MaxPosition=new(monitor.Work.Left-monitor.Monitor.Left,monitor.Work.Top-monitor.Monitor.Top);
                bounds.MaxSize=new(monitor.Work.Right-monitor.Work.Left,monitor.Work.Bottom-monitor.Work.Top);
                bounds.MinTrackSize=new((int)Math.Ceiling(window.MinWidth*dpi.DpiScaleX),(int)Math.Ceiling(window.MinHeight*dpi.DpiScaleY));
                Marshal.StructureToPtr(bounds,lParam,false);handled=true;return IntPtr.Zero;
            });
            Fit(window,true);
        };
        window.DpiChanged+=(_,_)=>window.Dispatcher.BeginInvoke(new Action(()=>Fit(window,false)));
    }
    private static void Fit(Window window,bool initial)
    {
        var work=WorkArea(window);window.MinWidth=Math.Max(1,Math.Min(900,work.Width-24));window.MinHeight=Math.Max(1,Math.Min(650,work.Height-24));
        if(window.WindowState!=WindowState.Normal)return;
        var preferred=PreferredSize(work.Size);window.Width=initial?preferred.Width:Math.Min(window.Width,preferred.Width);window.Height=initial?preferred.Height:Math.Min(window.Height,preferred.Height);
        if(initial){window.Left=work.Left+(work.Width-window.Width)/2;window.Top=work.Top+(work.Height-window.Height)/2;}
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint {public int X,Y;public NativePoint(int x,int y){X=x;Y=y;}}
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo {public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
}
