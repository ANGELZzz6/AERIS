using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IslasEscritorio.Core;

/// <summary>
/// Optimizador inteligente de memoria RAM para Windows utilizando EmptyWorkingSet y recolección de basura administrada.
/// </summary>
public static class MemoryOptimizer
{
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system",
        "idle",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "dwm",
        "explorer",
        "aeris",
        "devenv",
        "taskhostw",
        "fontdrvhost",
        "dwm.exe",
        "explorer.exe",
        "aeris.exe"
    };

    public static (int optimizedCount, long freedBytesEstimate) OptimizeMemory()
    {
        int optimized = 0;
        long totalFreedBytes = 0;

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                    continue;

                string name;
                try
                {
                    name = process.ProcessName;
                }
                catch
                {
                    continue;
                }

                if (ExcludedProcessNames.Contains(name))
                    continue;

                long workingSetBefore = 0;
                try
                {
                    workingSetBefore = process.WorkingSet64;
                }
                catch { }

                if (EmptyWorkingSet(process.Handle))
                {
                    optimized++;
                    try
                    {
                        long workingSetAfter = process.WorkingSet64;
                        if (workingSetBefore > workingSetAfter)
                        {
                            totalFreedBytes += (workingSetBefore - workingSetAfter);
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                // Algunos procesos no permiten modificar su memoria o ya terminaron.
            }
            finally
            {
                try
                {
                    process.Dispose();
                }
                catch { }
            }
        }

        // Recolección de basura administrada propia
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch { }

        return (optimized, totalFreedBytes);
    }
}
