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
            // todo: obezbediti da neko ne može sluuučajno da pristupi npr. system32 :)

            // primer zlonamernog url-a:
            // http://localhost:5050/%2e%2e%2f%2e%2e%2fwindows/win.ini

            // izdvajamo apsolutnu putanju do root foldera, koja mora da se završi sa / ili \
            string absoluteRoot = Path.GetFullPath(_rootPath);
            if (!absoluteRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                absoluteRoot += Path.DirectorySeparatorChar;
            }
            // izdvojeno ime fajla, bez direktorijuma prethodno
            string safeFileName = Path.GetFileName(fileName);
            // spajamo root i ime fajla, ali bez .. ili relativnih segmenata
            string fullPath = Path.GetFullPath(Path.Combine(absoluteRoot, safeFileName));

            // da li je dobijena putanja unutar root foldera?
            if(!fullPath.StartsWith(absoluteRoot, StringComparison.OrdinalIgnoreCase))
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
                    /* => preuzima se kao binarni fajl, ali je sadrzaj tekstualni fajl
                    //Uzimamo tekst iz fajla
                    string content = Encoding.UTF8.GetString(data);
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
            // ukoliko tražena ekstenzija fajla nije validna, treba vratiti grešku
            throw new NotSupportedException($"Extension {extension} is not supported.");           
        }
    }
}