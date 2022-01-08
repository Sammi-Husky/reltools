using BrawlLib.Internal;
using System.Diagnostics;
using System.Text;

namespace reltools
{
    public struct ProcResult
    {
        public ProcResult(string output, string error, int errStatus)
        {
            StandardOutput = output;
            StandardError = error;
            ExitCode = errStatus;
        }
        public readonly string StandardOutput;
        public readonly string StandardError;
        public readonly int ExitCode;
    }
    public static class Util
    {
        public static ProcResult StartProcess(string application, params string[] args)
        {
            var sb = new StringBuilder();
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = application,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();


            //while (!proc.StandardError.EndOfStream)
            //sb.Append(proc.StandardError.ReadLine() + "\n");
            //while (!proc.StandardOutput.EndOfStream)
            //sb.Append(proc.StandardOutput.ReadLine() + "\n");
            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            //result.StandardError = proc.StandardError.ReadToEnd();
            //sb.Append(proc.StandardOutput.ReadToEnd());
            int exitCode = proc.ExitCode;
            proc.Close();
            return new ProcResult(output, error, exitCode);
        }
    }
}
