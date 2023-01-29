namespace ForspokenTextTool
{
    public class TextEntry
    {
        public UInt16 InternalId { get; set; }
        public Int16 StringTypeConstant { get; set; }
        public Int32 StringNumber { get; set; }

        public Int32 Offset1 { get; set; }
        public Int32 Offset2 { get; set; }
        public Int32 Offset3 { get; set; }

        public Int32 Zero { get; set; }
        public Int32 One { get; set; }

        public string Component1 { get; set; }
        public string Component2 { get; set; }
        public string Component3 { get; set; }

        public string StringId { get; set; }
    }
}
