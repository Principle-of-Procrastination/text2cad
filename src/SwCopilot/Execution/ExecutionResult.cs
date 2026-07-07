using System.Collections.Generic;

namespace SwCopilot.Execution
{
    /// <summary>
    /// Structured result returned by every executor operation (spec section 9).
    /// The executor never throws to the UI; failures come back as Success=false.
    /// </summary>
    public sealed class ExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> CreatedFeatures { get; set; }
        public List<string> ModifiedDimensions { get; set; }
        public string Error { get; set; }

        public ExecutionResult()
        {
            CreatedFeatures = new List<string>();
            ModifiedDimensions = new List<string>();
        }

        public static ExecutionResult Ok(string message)
        {
            return new ExecutionResult { Success = true, Message = message };
        }

        public static ExecutionResult Fail(string message, string error = null)
        {
            return new ExecutionResult { Success = false, Message = message, Error = error };
        }
    }
}
