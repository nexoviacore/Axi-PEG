namespace AxPeg.Dtos.Request
{
    public class InitiateRequest
    {
        public string AppName { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public string IndexNo { get; set; } = string.Empty;
        public string KeyValue { get; set; } = string.Empty;
    }
}
