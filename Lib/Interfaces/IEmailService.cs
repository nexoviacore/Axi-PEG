using System.Threading.Tasks;

namespace AxPeg.Lib.Interfaces
{
    public interface IEmailService
    {
        Task SendMailAsync(string toEmail, string subject, string body, bool isHtml = true);
    }
}
