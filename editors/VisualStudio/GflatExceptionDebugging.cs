using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.CustomRuntimes;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Exceptions;
using Microsoft.VisualStudio.Debugger.Native;

namespace gflat.VisualStudio;

// A monitor component translates our runtime notification to a real debugger
// exception. Concord owns stopping, continuation, and Exception Settings.
public sealed class GflatExceptionDebugging : IDkmDebugMonitorExceptionNotification,
    IDkmExceptionFormatter, IDkmExceptionDetailsProvider
{
    // Use the native category: its supported settings path can select individual
    // gflat type names, and existing C++ exceptions keep their normal behavior.
    internal static readonly Guid Category = DkmExceptionCategory.Cpp;
    internal const uint NotificationCode = 0xE047464C;
    private const string Marker = "gflat:exception:1\0";
    private static bool IsGflat(DkmExceptionInformation exception)
    {
        if (!(exception is DkmCustomExceptionInformation custom) || custom.AdditionalInformation == null || custom.AdditionalInformation.Count < Marker.Length) return false;
        for (int i = 0; i < Marker.Length; i++) if (custom.AdditionalInformation[i] != Marker[i]) return false;
        return true;
    }

    public void OnDebugMonitorException(DkmExceptionInformation exception, DkmWorkList workList, DkmEventDescriptorS eventDescriptor)
    {
        if (!(exception is DkmWin32ExceptionInformation native) || native.Code != NotificationCode || native.ExceptionParameters.Count != 1) return;
        try
        {
            byte[] Read(ulong address, int count)
            {
                var buffer = new byte[count];
                int read;
                try { read = exception.Process.ReadMemory(address, DkmReadMemoryFlags.AllowPartialRead, buffer); }
                catch (DkmException) { return Array.Empty<byte>(); }
                catch (COMException) { return Array.Empty<byte>(); }
                if (read != count) Array.Resize(ref buffer, read);
                return buffer;
            }
            var report = GflatExceptionReport.Read(Read, native.ExceptionParameters[0]);
            if (report == null) return;
            var stage = report.Unhandled ? DkmExceptionProcessingStage.Unhandled : DkmExceptionProcessingStage.Thrown;
            var custom = DkmCustomExceptionInformation.Create(native.RuntimeInstance, Category, native.Thread,
                native.InstructionAddress, report.Type, 0, stage, native,
                new ReadOnlyCollection<byte>(Encoding.UTF8.GetBytes(Marker + report.Message)));
            custom.OnDebugMonitorException();
            eventDescriptor.Suppress();
        }
        catch (DkmException) { /* Leave an unreadable/unknown notification to the native debugger. */ }
        catch (COMException) { }
    }

    public string GetDescription(DkmExceptionInformation exception) => IsGflat(exception)
        ? exception.Name + ": " + GetAdditionalInformation(exception) : exception.GetDescription();
    public string GetAdditionalInformation(DkmExceptionInformation exception)
        => IsGflat(exception)
            ? Encoding.UTF8.GetString(new List<byte>(((DkmCustomExceptionInformation)exception).AdditionalInformation).ToArray(), Marker.Length,
                ((DkmCustomExceptionInformation)exception).AdditionalInformation!.Count - Marker.Length)
            : exception.GetAdditionalInformation()!;
    public DkmExceptionDetails GetExceptionDetails(DkmExceptionInformation exception, DkmInspectionSession inspectionSession)
        => IsGflat(exception) ? DkmExceptionDetails.Create(inspectionSession, exception, null) : exception.GetExceptionDetails(inspectionSession);
    public string GetFormattedDescription(DkmExceptionDetails details) => IsGflat(details.Exception) ? GetDescription(details.Exception) : details.GetFormattedDescription();
    public string GetExceptionMessage(DkmExceptionDetails details) => IsGflat(details.Exception) ? GetAdditionalInformation(details.Exception) : details.GetExceptionMessage()!;
    public string GetTypeName(DkmExceptionDetails details, bool fullName) => IsGflat(details.Exception) ? details.Exception.Name ?? "gflat exception" : details.GetTypeName(fullName);
    public string GetSource(DkmExceptionDetails details) => IsGflat(details.Exception) ? null! : details.GetSource()!;
    public int GetHResult(DkmExceptionDetails details) => IsGflat(details.Exception) ? 0 : details.GetHResult();
    public string GetStackTrace(DkmExceptionDetails details) => IsGflat(details.Exception) ? null! : details.GetStackTrace()!;
    public DkmExceptionDetails GetInnerException(DkmExceptionDetails details) => IsGflat(details.Exception) ? null! : details.GetInnerException()!;
    public string GetExceptionObjectExpression(DkmExceptionDetails details) => IsGflat(details.Exception) ? null! : details.GetExceptionObjectExpression()!;
}

