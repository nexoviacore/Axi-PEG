using System;
using System.Text;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using AxExtend.Interface;
using AxPeg.Services.Interfaces;
using AxPeg.Exceptions;
using Serilog;

namespace AxPeg.Services
{
    public class SBPegRestService : ISBPegRestService
    {
        private readonly IAxExtend _axExtend;

        public SBPegRestService(IAxExtend axExtend)
        {
            _axExtend = axExtend;
        }

        public async Task<string> ConnectToProjectAsync(string db)
        {
            try
            {
                Log.Information("Connecting to database project: {Db}", db);
                bool success = await _axExtend.OpenDBConnectionAsync(db);
                if (success)
                {
                    return "Connection successful";
                }
                return "Connection failed";
            }
            catch (Exception ex)
            {
                throw new DatabaseException($"Error connecting to project database: {db}", ex);
            }
        }

        public async Task<string> CloseProjectAsync()
        {
            try
            {
                Log.Information("Closing project database connection.");
                await _axExtend.CloseDBConnectionAsync();
                return "Connection closed successfully";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error occurred while closing project database connection.");
                return "Error closing connection";
            }
        }

        public string MakeValidJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        public string RemoveSplitCharAndMakeValidJsonStr(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            // Legacy Delphi logic replaces custom split character sequences (e.g. `^` or `~`)
            return MakeValidJsonString(input.Replace("^", "").Replace("~", ""));
        }

        public async Task<string> LoadXMLDataFromWSAsync(string xml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(xml)) return string.Empty;
                var doc = XDocument.Parse(xml);
                return doc.ToString();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load and parse XML data.");
                return string.Empty;
            }
        }

        public async Task<string> ConvertJSONToXMLDocAsync(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return string.Empty;

                var node = JsonNode.Parse(json);
                var xmlBuilder = new StringBuilder();
                xmlBuilder.Append("<root>");

                if (node is JsonObject obj)
                {
                    foreach (var property in obj)
                    {
                        string val = property.Value?.ToString() ?? string.Empty;
                        xmlBuilder.Append($"<{property.Key}>{System.Security.SecurityElement.Escape(val)}</{property.Key}>");
                    }
                }
                xmlBuilder.Append("</root>");

                return await Task.FromResult(xmlBuilder.ToString());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to convert JSON payload to XML Document format.");
                return string.Empty;
            }
        }
    }
}
