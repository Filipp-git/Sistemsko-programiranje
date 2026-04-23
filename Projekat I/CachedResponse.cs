using System.Diagnostics;

namespace ProjekatI
{
    public class CachedResponse
    {
        public byte[] Data { get; }
        public string ContentType { get; }
        public string DownloadName { get; } // null ako se fajl ne preuzima
        // za implementaciju vremenskog isticanja
        public DateTime CreatedAt { get; }
        // koliko milisekundi je bilo potrebno za obradu, kada se javi promašaj u kešu?
        public long ProcessingTime { get; }
        public CachedResponse(byte[] data, string contentType, long processingTime, string downloadName = null)
        {
            Data = data;
            ContentType = contentType;
            ProcessingTime = processingTime;
            DownloadName = downloadName;
            CreatedAt = DateTime.Now;
        }
    }
}