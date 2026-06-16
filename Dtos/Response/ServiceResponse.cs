namespace AxPeg.Dtos.Response
{
    public class ServiceResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }

        public static ServiceResponse<T> Ok(T data, string message = "Success")
        {
            return new ServiceResponse<T> { Success = true, Message = message, Data = data };
        }

        public static ServiceResponse<T> Fail(string message)
        {
            return new ServiceResponse<T> { Success = false, Message = message };
        }
    }
}
