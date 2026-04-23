namespace ProjekatI
{
    public class CachedResponse
    {
        public byte[] Data { get; }
        public string ContentType { get; }
        public string DownloadName { get; } // null ako se fajl ne preuzima
        // za implementaciju vremenskog isticanja
        public DateTime CreatedAt { get; }
        public CachedResponse(byte[] data, string contentType, string downloadName = null)
        {
            Data = data;
            ContentType = contentType;
            DownloadName = downloadName;
            CreatedAt = DateTime.Now;
        }
    }
}