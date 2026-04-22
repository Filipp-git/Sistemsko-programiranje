using System;
using System.IO;
using System.Text;

namespace ProjekatI
{
    public class FileConverter
    {
        private readonly string _rootPath;

        public FileConverter(string rootPath)
        {
            _rootPath = rootPath;
        }

        public byte[] ProcessFile(string fileName)
        {
            string fullPath = Path.Combine(_rootPath, fileName);

            if(!File.Exists(fullPath))
                throw new FileNotFoundException($"File {fileName} not found");
            
            byte[] data = File.ReadAllBytes(fullPath); 
            string extension = Path.GetExtension(fullPath).ToLower();

            //Koneverzija bin=>txt
            if(extension == ".bin")
            {
                //Pretvaramo binarne podatke u Base64 (tekstualna reprezentacija binarnih podataka) string za prikaz u browseru
                string base64String = Convert.ToBase64String(data);
                //Console.WriteLine($".bin=>.txt on file: {fileName}");
                Logger.Log($".bin=>.txt conversion: {fileName}");
                return Encoding.UTF8.GetBytes(base64String);
            }

            //Koneverzija txt=>bin
            if(extension == ".txt")
            {
                try 
                {
                    /* => preuzima se kao binarni fajl, ali je sadrzaj tekstualni fajl
                    //Uzimamo tekst iz fajla
                    string content = Encoding.UTF8.GetString(data);
                    //Console.WriteLine($".txt=>.bin on file: {fileName}");
                    Logger.Log($".txt=>.bin on file: {fileName}");
                    //Pretvaramo Base64 tekst nazad u sirove bajtove
                    return Convert.FromBase64String(content);
                    */

                    /*
                    //string content = Encoding.UTF8.GetString(data);
                    string content = File.ReadAllText(fullPath);

                    Logger.Log($".txt=>.bin conversion: {fileName}");

                    return Encoding.UTF8.GetBytes(content);*/

                    return data;
                }
                catch (FormatException)
                {
                    //Ako fajl nije validan Base64, samo vrati originalne bajtove 
                    //da se program ne bi srušio
                    return data;
                }
            }

            return data; //Nadamo se da nikada nece da dodje do ovog dela
        }
    }
}