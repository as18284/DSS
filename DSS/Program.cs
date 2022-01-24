using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DSS 
{
    public class Program
    {

        static List<KeyValuePair<int, int>> pointers;
        static List<KeyValuePair<int, int>> indexes;

        static int first;
        static int second;
        static int nextPart;

        public static void HandleDefaultValues(ref int firstVal, ref int secondVal)
        {
            if (firstVal == -1)
                firstVal = first;

            if (secondVal == -1)
                secondVal = second;
        }

        /// <summary>
        /// Nosaka vai first un second nav izgājuši ārpus intervāla robežām.
        /// </summary>
        /// <param name="firstVal"></param>
        /// <param name="secondVal"></param>
        /// <returns></returns>
        public static bool InCurrentIntervalBoundrys(int firstVal = - 1, int secondVal = - 1)
        {
            HandleDefaultValues(ref firstVal, ref secondVal);
            return firstVal < pointers[nextPart - 1].Value && secondVal < pointers[nextPart].Value;
        }

        /// <summary>
        /// Nosaka vai mainīgie kuru indeksus satur first un second reprezentē to pašu dokumentu.
        /// </summary>
        /// <param name="firstVal"></param>
        /// <param name="secondVal"></param>
        /// <returns></returns>
        public static bool IsSameDocumentInIntervals(int firstVal = -1, int secondVal = -1)
        {
            HandleDefaultValues(ref firstVal, ref secondVal);
            return indexes[firstVal].Key == indexes[secondVal].Key;
        }

        /// <summary>
        /// Konsolē izdrukā rezultātu, kad dokumentā veiksmīgi atrasta lietotāja meklētā simbolu virkne. <br/>
        /// Rezultātā izdrukā dokumenta nosaukumu, simbolu virknes pozīciju, dokumenta pilno ceļu un dokumenta pēdējo rediģēšanas datumu.
        /// </summary>
        /// <param name="DocumentId"></param>
        /// <param name="Position"></param>
        /// <param name="SearchQuery"></param>
        public static void PrintResult(int DocumentId, int Position, string SearchQuery)
        {
            using (var context = new DocumentIndexEntities())
            {
                var docData = context.DocumentMetadata.FirstOrDefault(x => x.DocumentId == DocumentId);
                if (docData != null)
                {
                    string docName = Regex.Match(docData.DocumentAbsolutePath, @"[\w\s]*[.].*$")?.Value ?? "";
                    Console.WriteLine("Found string \"{0}\" occurrence in document: \"{1}\" at position: {2} ", SearchQuery, docName, Position);
                    Console.WriteLine("Document absolute path: {0}", docData.DocumentAbsolutePath);
                    Console.WriteLine("Document author: {0}", docData.DocumentAuthor);
                    Console.WriteLine("Document last edit time: {0}", docData.DocumentLastEditTime);
                    Console.WriteLine();
                }
            }
        }

        /// <summary>
        /// Atgriež no datu bāzes tabulas WordPosition lielākā Id + 1 int vērtību. 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static int GetLastWordPositionIdP1(DocumentIndexEntities context)
        {
            string sqlQuery =
                "SELECT * FROM WordPosition " +
                "WHERE Id = (SELECT MAX(Id) FROM WordPosition)";

            return context.WordPosition.SqlQuery(sqlQuery).ToList()
                .FirstOrDefault()?.Id + 1 ?? 1;
        }

        /// <summary>
        /// Izveido datu bāzes tabulā WordPosition jaunu ierakstu. <br/>
        /// Pēdējam iepriekšējam ierakstam NextPosition noglabā saiti uz jauno izveidoto ierakstu. <br/>
        /// Šo funkciju lieto gadījumos, kad vārds NodeIndex tabulā jau eksistē.
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="position"></param>
        /// <param name="dbWord"></param>
        /// <param name="context"></param>
        public static void CreateWordPosition(int documentId, int position, NodeIndex dbWord, DocumentIndexEntities context)
        {
            int newPosId = GetLastWordPositionIdP1(context);

            context.WordPosition.Add(
                new DSS.WordPosition
                {
                    Id = newPosId,
                    DocumentIndex = documentId,
                    Position = position
                }
            );

            DSS.WordPosition lastPosition = dbWord.WordPosition;
            for (; lastPosition.WordPosition2 != null; lastPosition = lastPosition.WordPosition2) ;

            lastPosition.NextPosition = newPosId;
        }

        /// <summary>
        /// Izveido datu bāzes tabulās WordPosition un NodeIndex jaunus ierakstus. <br/>
        /// Pēdējam iepriekšējam ierakstam NextPosition noglabā saiti uz jauno izveidoto ierakstu. <br/>
        /// Šo funkciju lieto gadījumos, kad vārds NodeIndex tabulā neeksistē.
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="position"></param>
        /// <param name="word"></param>
        /// <param name="context"></param>
        public static void CreateNodeIndex(int documentId, int position, string word, DocumentIndexEntities context)
        {
            int newPosId = GetLastWordPositionIdP1(context);

            context.WordPosition.Add(
                new DSS.WordPosition
                {
                    Id = newPosId,
                    DocumentIndex = documentId,
                    Position = position
                }
            );

            context.NodeIndex.Add(
                new DSS.NodeIndex
                {
                    Id = word.GetHashCode(),
                    Word = word,
                    PositionsList = newPosId
                }
            );
        }

        /// <summary>
        /// Izveido datu bāzes tabulā DocumentMetadata jaunu ierakstu.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="documentId"></param>
        /// <param name="context"></param>
        public static void CreateMetaData(object filePath, int documentId, DocumentIndexEntities context)
        {
            var lastModified = File.GetLastWriteTime((string)filePath);
            context.DocumentMetadata.Add(new DocumentMetadata
            {
                DocumentLastEditTime = lastModified,
                DocumentId = documentId,
                DocumentAbsolutePath = (string)filePath
            });
        }

        /// <summary>
        /// Sagatavo dokumentu priekš apstrādes.
        /// Izveido un atgriež Document interfeisa objektu.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="documentId"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public static Document HandleDocument(ref object file, int documentId, DocumentIndexEntities context)
        {
            Application wordApp = new Application();
            object nullobj = Missing.Value;
            object readOnly = true;
            Document doc = 
                wordApp.Documents.Open(ref file, nullobj, ReadOnly: readOnly,
                    nullobj, nullobj, nullobj,
                    nullobj, nullobj, nullobj,
                    nullobj, nullobj, nullobj,
                    nullobj, nullobj, nullobj, nullobj
                );
            ExtractMetadata(doc.BuiltInDocumentProperties, documentId, context);

            return doc;
        }

        /// <summary>
        /// Veic meklēšanu datu bāzē esošajā dokumentu indeksa struktūrā atbilstoši lietotāja ievadītajai simbolu virknei.
        /// </summary>
        /// <param name="input"></param>
        public static void LinearSearch(string input)
        {
            using (var context = new DocumentIndexEntities())
            {
                int last = 0;
                indexes = new List<KeyValuePair<int, int>>();
                pointers = new List<KeyValuePair<int, int>>();

                foreach (string str in input.Split(' '))
                {
                    int hash = str.GetHashCode();
                    var item = context.NodeIndex.Where( x => x.Id == hash ).FirstOrDefault();
                    if (item == null)
                    {
                        Console.WriteLine("No match for string \"{0}\" is found in document collection", input);
                        return;
                    }

                    int wordPositionsCount = 0;
                    for (DSS.WordPosition pt = item.WordPosition; pt != null; pt = pt.WordPosition2)
                    {
                        indexes.Add(new KeyValuePair<int, int>((int)pt.DocumentIndex, (int)pt.Position));
                        wordPositionsCount++;
                    }

                    pointers.Add(new KeyValuePair<int, int>(last, last + wordPositionsCount));
                    last += wordPositionsCount;
                }

                first = pointers[0].Key;
                second = pointers[1].Key;
                nextPart = 1;

                while (InCurrentIntervalBoundrys())
                {
                    while (InCurrentIntervalBoundrys() && IsSameDocumentInIntervals())
                    {
                        if (indexes[first].Value + 1 == indexes[second].Value)
                        {
                            nextPart++;
                            bool isLastInterval = nextPart == pointers.Count;
                            if (isLastInterval)
                            {
                                PrintResult(indexes[first].Key, indexes[first].Value, input);
                                nextPart = 1;
                                first = pointers[0].Key;
                                first++;
                                pointers[0] = new KeyValuePair<int, int>(first, pointers[0].Value);

                                second = pointers[1].Key;
                                second++;
                                pointers[1] = new KeyValuePair<int, int>(second, pointers[1].Value);

                                continue;
                            }
                            first = second;
                            second = pointers[nextPart].Key;
                            continue;
                        }
                        if (InCurrentIntervalBoundrys(first + 1, second + 1) && 
                            IsSameDocumentInIntervals(first + 1, second + 1)
                            )
                        {
                            //Ja nākamā sektora tā paša dokumenta indeks ir lielāks un nebija lielāks par +1, tad var abus pointerus palielināt par viens, jo tad ir zināms, ka frāze tur neizveidojas.
                            if (indexes[second].Value > indexes[first].Value)
                            {
                                first++;
                                pointers[nextPart - 1] = new KeyValuePair<int, int>(first, pointers[nextPart - 1].Value);
                            }
                            if (indexes[second].Value < indexes[first].Value)
                            {
                                second++;
                                pointers[nextPart] = new KeyValuePair<int, int>(second, pointers[nextPart].Value);
                            }
                        }
                        else if (second + 1 < pointers[nextPart].Value &&
                            !IsSameDocumentInIntervals(first, second + 1))
                        {
                            nextPart = 1;
                            first = pointers[0].Key;
                            first++;
                            pointers[0] = new KeyValuePair<int, int>(first, pointers[0].Value);

                            second = pointers[1].Key;
                            second++;
                            pointers[1] = new KeyValuePair<int, int>(second, pointers[1].Value);
                        }
                        else
                        {
                            second++;
                            pointers[nextPart] = new KeyValuePair<int, int>(second, pointers[nextPart].Value);
                        }
                    }

                    if (!InCurrentIntervalBoundrys())
                        break;

                    if (nextPart >= 2 && !(indexes[pointers[nextPart - 2].Key].Key == indexes[pointers[nextPart - 1].Key].Key)
                            && nextPart != 1)
                    {
                        nextPart = 1;
                        first = pointers[0].Key;
                        first++;
                        pointers[0] = new KeyValuePair<int, int>(first, pointers[0].Value);

                        second = pointers[1].Key;
                        second++;
                        pointers[1] = new KeyValuePair<int, int>(second, pointers[1].Value);
                        continue;
                    }

                    if (indexes[first].Key < indexes[second].Key)
                    {
                        first++;
                        pointers[nextPart - 1] = new KeyValuePair<int, int>(first, pointers[nextPart - 1].Value);
                    }
                    else
                    {
                        second++;
                        pointers[nextPart] = new KeyValuePair<int, int>(second, pointers[nextPart].Value);
                    }
                }
            }
        }

        /// <summary>
        /// Iegūst dokumenta autora vārdu un noglabā to datu bāzē.
        /// </summary>
        /// <param name="wordProperties"></param>
        /// <param name="docId"></param>
        /// <param name="context"></param>
        public static void ExtractMetadata(object wordProperties, int docId, DocumentIndexEntities context)
        {
            Type typeDocBuiltInProps = wordProperties.GetType();
            BindingFlags bindFlags = BindingFlags.Default | BindingFlags.GetProperty;

            object Authorprop = typeDocBuiltInProps.
                InvokeMember("Item", bindFlags, null, wordProperties, new object[] { "Author" });
            
            Type typeAuthorprop = Authorprop.GetType();

            string strAuthor = typeAuthorprop.
                InvokeMember("Value", bindFlags, null, Authorprop, new object[] { })?.ToString();

            var docMetadata = context.DocumentMetadata.FirstOrDefault(x => x.DocumentId == docId);
            if (docMetadata != null && !String.IsNullOrEmpty(strAuthor))
                docMetadata.DocumentAuthor = strAuthor;
        }

        /// <summary>
        /// Izveido datu bāzē dokumentu indeksa struktūru atbilstoši padotajam failam.
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="file"></param>
        /// <param name="context"></param>
        public static void MakeIndex(int documentId, object file, DocumentIndexEntities context)
        {
            var doc = HandleDocument(ref file, documentId, context);

            int position = 0;
            for (int i = 1; i <= doc.Paragraphs.Count; i++)
            {
                int textLength = doc.Paragraphs[i].Range.Text.Length;
                string paragraph = doc.Paragraphs[i].Range.Text.Substring(0, textLength - 1);
                if (string.IsNullOrEmpty(paragraph))
                    continue;

                string wordString = Regex.Replace(paragraph.ToLower(), @"[^\w\s]|\t|\n|\r", "");

                foreach (string word in wordString.Split(' ').Where(x => x.Length > 0 && !string.IsNullOrWhiteSpace(x)))
                {
                    var dbWord = context.NodeIndex.FirstOrDefault(x => word.Equals(x.Word));
                    if (dbWord != null)
                    {
                        CreateWordPosition(documentId, position, dbWord, context);
                    }
                    else
                    {
                        CreateNodeIndex(documentId, position, word, context);
                    }
                    position++;
                    context.SaveChanges();
                }

            }
            object saveChanges = false;
            doc.Close(saveChanges);
            
        }


        /// <summary>
        /// Apstaigā visas padotās direktorijas datnes un arī visas apakšdirektoriju datnes. <br/>
        /// Indeksa struktūrā tiek iekļauti dokumenti tikai ar paplašinājumu docx vai doc.
        /// </summary>
        /// <param name="directoryPath"></param>
        public static void CrawlDirectoryForDocx(string directoryPath)
        {
            using (var context = new DocumentIndexEntities())
            {
                IEnumerable<string> directoryFiles = new List<string>();
                try
                {
                    directoryFiles =
                        Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)?
                        .Where(x => Regex.IsMatch(x, @".*docx|.*doc"));
                }
                catch (DirectoryNotFoundException Execption)
                {
                    Console.WriteLine(Execption.Message);
                    return;
                }

                if (directoryFiles.Count() == 0)
                {
                    Console.WriteLine("No documents in given directory");
                    return;
                }

                int documentId = 0;
                foreach (object filePath in directoryFiles)
                {
                    CreateMetaData(filePath, documentId, context);
                    context.SaveChanges();
                    MakeIndex(documentId, filePath, context);
                    documentId++;
                }
            }

        }

        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.Unicode;
            //string directoryPath = @"C:\Users\Arnolds\Desktop\7semestris\KD Kursa darbs\DSS\DSS\DSS\";
            //CrawlDirectoryForDocx(directoryPath);
            string userInput = "";
            while (userInput != "0")
            {
                Console.WriteLine("Please enter search query:");
                userInput = Console.ReadLine();
                Console.WriteLine();
                if (!string.IsNullOrEmpty(userInput) && userInput != "0")
                {
                    LinearSearch(userInput);
                }
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to end application...");
            Console.ReadKey();
        }

    }
}
