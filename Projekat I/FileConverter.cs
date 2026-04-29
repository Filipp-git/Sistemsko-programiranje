using System;
using System.IO;
using System.Text;

namespace ProjekatI
{
    public class FileConverter
    {
        //Putanja do foldera sa fajlovima
        private readonly string _rootPath;

        public FileConverter(string rootPath)
        {
            _rootPath = rootPath;
        }

        public byte[] ProcessFile(string fileName)
        {
            // primer zlonamernog url-a:
            // http://localhost:5050/%2e%2e%2f%2e%2e%2fwindows/win.ini

            // izdvajamo apsolutnu putanju do root foldera, koja mora da se završi sa / ili \
            string absoluteRoot = Path.GetFullPath(_rootPath);
            if (!absoluteRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                absoluteRoot += Path.DirectorySeparatorChar; //osiguravamo da se putanja zavrsava sa \ ili /
            }
            // izdvojeno ime fajla, bez direktorijuma prethodno
            string safeFileName = Path.GetFileName(fileName);
            // spajamo root i ime fajla, ali bez .. ili relativnih segmenata
            string fullPath = Path.GetFullPath(Path.Combine(absoluteRoot, safeFileName));

            // da li je dobijena putanja unutar root foldera? ili je neko pokusao nesto sumjnivo
            if (!fullPath.StartsWith(absoluteRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Attempt to break out of the root directory!");
            }

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"File {fileName} not found");

            byte[] data = File.ReadAllBytes(fullPath);
            string extension = Path.GetExtension(fullPath).ToLower();

            //Koneverzija bin=>txt
            if (extension == ".bin")
            {
                //Pretvaramo binarne podatke u Base64 (tekstualna reprezentacija binarnih podataka) string za prikaz u browseru
                string base64String = Convert.ToBase64String(data);

                Logger.Log($".bin=>.txt conversion: {safeFileName}");
                return Encoding.UTF8.GetBytes(base64String);
            }

            //Koneverzija txt=>bin
            else if (extension == ".txt")
            {
                try
                {
                    return data;
                }
                catch (FormatException)
                {
                    //Ako fajl nije validan Base64, samo vrati originalne bajtove 
                    //da se program ne bi srušio
                    return data;
                }
            }
            // ukoliko tražena ekstenzija fajla nije validna, treba vratiti grešku
            throw new NotSupportedException($"Extension {extension} is not supported.");
        }
    }
}