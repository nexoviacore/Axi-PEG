using System.Threading.Tasks;

namespace AxPeg.Services.Interfaces
{
    public interface ISBPegRestService
    {
        Task<string> LoadXMLDataFromWSAsync(string xml);
        Task<string> ConvertJSONToXMLDocAsync(string json);
        Task<string> ConnectToProjectAsync(string db);
        Task<string> CloseProjectAsync();
        string MakeValidJsonString(string input);
        string RemoveSplitCharAndMakeValidJsonStr(string input);
    }
}
