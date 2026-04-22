namespace ProjekatI
{
    public class CachedResponse
    {
        public byte[] Data { get; }
        public string ContentType { get; }
        public string DownloadName { get; } // null ako se fajl ne preuzima
        // todo: verovatno dodati datum upisa
        public CachedResponse(byte[] data, string contentType, string downloadName = null)
        {
            Data = data;
            ContentType = contentType;
            DownloadName = downloadName;
        }
    }
}