using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class GflatDebuggerTest
{
    // Standard OLE automation retry handling while VS is building/initializing.
    // https://learn.microsoft.com/en-us/previous-versions/visualstudio/visual-studio-2010/ms228772(v=vs.100)
    [ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMessageFilter
    {
        [PreserveSig] int HandleInComingCall(int type, IntPtr caller, int tick, IntPtr info);
        [PreserveSig] int RetryRejectedCall(IntPtr callee, int tick, int rejectType);
        [PreserveSig] int MessagePending(IntPtr callee, int tick, int pendingType);
    }
    private sealed class AutomationFilter : IMessageFilter
    {
        public int HandleInComingCall(int type, IntPtr caller, int tick, IntPtr info) { return 0; }
        public int RetryRejectedCall(IntPtr callee, int tick, int rejectType) { return rejectType == 2 && tick < 30000 ? 100 : -1; }
        public int MessagePending(IntPtr callee, int tick, int pendingType) { return 2; }
    }
    [DllImport("ole32.dll")] private static extern int CoRegisterMessageFilter(IMessageFilter filter, out IMessageFilter previous);
    private static IMessageFilter previousFilter;
    private static IMessageFilter filter;
    public static void RegisterFilter() { filter = new AutomationFilter(); Marshal.ThrowExceptionForHR(CoRegisterMessageFilter(filter, out previousFilter)); }
    public static void RestoreFilter() { IMessageFilter ignored; CoRegisterMessageFilter(previousFilter, out ignored); filter = null; }
    [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(int reserved, out IBindCtx context);

    public static object Find(int processId)
    {
        IRunningObjectTable table; IBindCtx context;
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out table));
        Marshal.ThrowExceptionForHR(CreateBindCtx(0, out context));
        IEnumMoniker enumerator; table.EnumRunning(out enumerator);
        var monikers = new IMoniker[1];
        try
        {
            while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
            {
                string name; monikers[0].GetDisplayName(context, null, out name);
                if (name.StartsWith("!VisualStudio.DTE.") && name.EndsWith(":" + processId))
                {
                    object value; table.GetObject(monikers[0], out value); return value;
                }
            }
            return null;
        }
        finally { Marshal.ReleaseComObject(enumerator); Marshal.ReleaseComObject(context); Marshal.ReleaseComObject(table); }
    }

    public static void Open(object raw, string directory, string project)
    {
        var dte = (EnvDTE.DTE)raw;
        dte.SuppressUI = true;
        dte.Solution.Create(directory, "probe");
        dte.Solution.AddFromFile(project, false);
        dte.Solution.SaveAs(directory + "/probe.sln");
        dte.Solution.SolutionBuild.StartupProjects = new object[] { "probe.gfproj" };
        dte.Solution.SolutionBuild.Build(true);
        if (dte.Solution.SolutionBuild.LastBuildInfo != 0) throw new Exception("Visual Studio build failed.");
    }

    public static void Breakpoint(object raw, string file, int line) { ((EnvDTE.DTE)raw).Debugger.Breakpoints.Add("", file, line); }
    public static void ClearBreakpoints(object raw)
    {
        var breakpoints = ((EnvDTE.DTE)raw).Debugger.Breakpoints;
        while (breakpoints.Count > 0) breakpoints.Item(1).Delete();
    }
    public static void Configure(object raw, bool thrown)
    {
        var group = ((EnvDTE90.Debugger3)((EnvDTE.DTE)raw).Debugger).ExceptionGroups.Item("C++ Exceptions");
        EnvDTE90.ExceptionSetting setting;
        try { setting = group.Item("Problem"); }
        catch (COMException) { setting = group.NewException("Problem", 0); }
        group.SetBreakWhenThrown(thrown, setting);
    }
    public static string Mode(object raw) { return ((EnvDTE.DTE)raw).Debugger.CurrentMode.ToString(); }
    public static string Reason(object raw) { return ((EnvDTE.DTE)raw).Debugger.LastBreakReason.ToString(); }
    public static int ProcessId(object raw) { return ((EnvDTE.DTE)raw).Debugger.CurrentProcess.ProcessID; }
    public static string Value(object raw, string expression) { return ((EnvDTE.DTE)raw).Debugger.GetExpression(expression, true, 1000).Value; }
    public static void Go(object raw) { ((EnvDTE.DTE)raw).Debugger.Go(false); }
    public static void Stop(object raw) { ((EnvDTE.DTE)raw).Debugger.Stop(false); }
    public static string Output(object raw)
    {
        var document = ((EnvDTE80.DTE2)raw).ToolWindows.OutputWindow.OutputWindowPanes.Item("Debug").TextDocument;
        return document.StartPoint.CreateEditPoint().GetText(document.EndPoint);
    }
    public static void Close(object raw) { ((EnvDTE.DTE)raw).Solution.Close(false); }
    public static void Quit(object raw) { ((EnvDTE.DTE)raw).Quit(); }
}
