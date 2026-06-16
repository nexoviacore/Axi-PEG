namespace AxPeg.Dtos.Request
{
    public class ActionRequest
    {
        public string AppName { get; set; } = string.Empty;
        public string TaskId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
        public string ForwardToUser { get; set; } = string.Empty;
    }
}
