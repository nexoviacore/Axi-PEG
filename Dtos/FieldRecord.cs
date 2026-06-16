namespace AxPeg.Dtos
{
    public class FieldRecord
    {
        public string TableName { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int RowNo { get; set; }
        public string Value { get; set; } = string.Empty;
        public double IdValue { get; set; }
        public string OldValue { get; set; } = string.Empty;
        public double OldIdValue { get; set; }
        public int FrameNo { get; set; }
        public double RecordId { get; set; }
        public char PrimaryTable { get; set; }
        public int Orders { get; set; }
        public int OldRow { get; set; }
        public bool SourceKey { get; set; }
        public int ParentRowNo { get; set; }
        public int OldParentRow { get; set; }
        public bool ZeroValue { get; set; }
        public bool AutoValue { get; set; }
        public bool ClientNotify { get; set; }
        public string WebValue { get; set; } = string.Empty;
        public string EncryptValue { get; set; } = string.Empty;
    }
}
