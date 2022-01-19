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
        public static void PrintResult(int DocumentId, int Position, string SearchQuery)
        {
            using (var context = new DocumentIndexEntities())
            {
                var docData = context.DocumentMetadata.FirstOrDefault(x => x.DocumentId == DocumentId);
                if (docData != null)
                {
                    string docName = Regex.Match(docData.DocumentAbsolutePath, @"\w*[.].*$")?.Value ?? "";
                    Console.WriteLine("Found string \"{0}\" occurrence in document: \"{1}\" at position: {2} ", SearchQuery, docName, Position);
                    Console.WriteLine("Document absolute path: {0}", docData.DocumentAbsolutePath);
                    Console.WriteLine("Document author: {0}", docData.DocumentAuthor);
                    Console.WriteLine("Document last edit time: {0}", docData.DocumentLastEditTime);
                    Console.WriteLine();
                }
            }
        }

        public static void LinearSearch(string input)
        {
            using (var context = new DocumentIndexEntities())
            {
                List<KeyValuePair<int, int>> pointers = new List<KeyValuePair<int, int>>();
                List<KeyValuePair<int, int>> indexes = new List<KeyValuePair<int, int>>();
                int wordCount = 0;
                int last = 0;

                foreach (string str in input.Split(' '))
                {
                    int hash = str.GetHashCode();
                    var item = context.NodeIndex.Where( x => x.Id == hash ).FirstOrDefault();
                    if (item == null)
                    {
                        Console.WriteLine("No match for string \"{0}\" is found in document collection", input);
                        return;
                    }
                        

                    List<KeyValuePair<int, int>> wordPositions = new List<KeyValuePair<int, int>>();
                    for (DSS.WordPosition pt = item.WordPosition; pt != null; pt = pt.WordPosition2)
                    {
                        wordPositions.Add(new KeyValuePair<int, int>((int)pt.DocumentIndex, (int)pt.Position));
                    }


                    indexes.AddRange(wordPositions);
                    pointers.Add(new KeyValuePair<int, int>(last, last + wordPositions.Count()));
                    last += wordPositions.Count();
                }
                wordCount = pointers.Count();

                int first = pointers[0].Key;
                int second = pointers[1].Key;
                int nextPart = 1;

                while (first < pointers[nextPart - 1].Value && second < pointers[nextPart].Value)
                {
                    while (first < pointers[nextPart - 1].Value && second < pointers[nextPart].Value &&
                        indexes[first].Key == indexes[second].Key)
                    {
                        if (indexes[first].Value + 1 == indexes[second].Value)
                        {
                            nextPart++;
                            if (nextPart == pointers.Count)
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
                        if (first + 1 < pointers[nextPart - 1].Value && second + 1 < pointers[nextPart].Value &&
                            indexes[first + 1].Key == indexes[second + 1].Key
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
                            indexes[first].Key != indexes[second + 1].Key)
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

                    if (!(first < pointers[nextPart - 1].Value && second < pointers[nextPart].Value))
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

        public static void ExtractMetadata(object wordProperties, int docId, DocumentIndexEntities context)
        {
            Type typeDocBuiltInProps = wordProperties.GetType();

            object Authorprop = typeDocBuiltInProps.
                InvokeMember("Item", BindingFlags.Default | BindingFlags.GetProperty, null, wordProperties, new object[] { "Author" });
            
            Type typeAuthorprop = Authorprop.GetType();

            string strAuthor = typeAuthorprop.
                InvokeMember("Value", BindingFlags.Default | BindingFlags.GetProperty, null, Authorprop, new object[] { })?.ToString();

            var docMetadata = context.DocumentMetadata.FirstOrDefault(x => x.DocumentId == docId);
            if (docMetadata != null && !String.IsNullOrEmpty(strAuthor))
                docMetadata.DocumentAuthor = strAuthor;
        }

        public static void MakeIndex(int documentId, ref int wordId, object file)
        {
            using (var context = new DocumentIndexEntities())
            {
                Microsoft.Office.Interop.Word.Application wordApp = new Microsoft.Office.Interop.Word.Application();
                object nullobj = System.Reflection.Missing.Value;
                object readOnly = true;
                Microsoft.Office.Interop.Word.Document doc =
                    wordApp.Documents.Open(ref file, nullobj, ReadOnly: readOnly,
                    nullobj, nullobj, nullobj,
                    nullobj, nullobj, nullobj,
                    nullobj, nullobj, nullobj,
                    nullobj, nullobj, nullobj, nullobj);

                ExtractMetadata(doc.BuiltInDocumentProperties, documentId, context);

                int position = 0;
                for (int i = 1; i <= doc.Paragraphs.Count; i++)
                {
                    string paragraph = doc.Paragraphs[i].Range.Text;
                    paragraph = paragraph.Replace("\r", "");
                    if (string.IsNullOrEmpty(paragraph))
                        continue;

                    string wordString = Regex.Replace(paragraph.ToLower(), @"[^\w\s]", "");

                    foreach (string word in wordString.Split(' ').Where(x => x.Length > 0 && !string.IsNullOrWhiteSpace(x)))
                    {
                        var dbWord = context.NodeIndex.FirstOrDefault(x => word.Equals(x.Word));
                        if (dbWord != null)
                        {                            
                            int lastNewId = context.WordPosition.ToList()?.LastOrDefault()?.Id + 1 ?? 1;
                            context.WordPosition.Add(
                                new DSS.WordPosition
                                {
                                    Id = lastNewId,
                                    DocumentIndex = documentId,
                                    Position = position
                                }
                            );

                            DSS.WordPosition lastPosition = dbWord.WordPosition;
                            for (; lastPosition.WordPosition2 != null; lastPosition = lastPosition.WordPosition2) ;

                            lastPosition.NextPosition = lastNewId;
                            context.SaveChanges();
                        }
                        else
                        {
                            var newPosId = context.WordPosition.ToList()?.LastOrDefault()?.Id + 1 ?? 1;

                            context.WordPosition.Add(
                                new DSS.WordPosition
                                {
                                    Id = newPosId,
                                    DocumentIndex = documentId,
                                    Position = position
                                }
                            );
                            context.SaveChanges();

                            context.NodeIndex.Add(
                                new DSS.NodeIndex
                                {
                                    Id = word.GetHashCode(),
                                    Word = word,
                                    PositionsList = newPosId
                                }
                            );

                            context.SaveChanges();
                            wordId++;
                        }
                        position++;
                    }

                }
                object saveChanges = false;
                doc.Close(saveChanges);

            }
        }

        public static void CrawlDirectoryForDocx(string directoryPath)
        {
            using (var context = new DocumentIndexEntities())
            {
                var directoryFiles =
                    Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)?
                    .Where(x => Regex.IsMatch(x, @".*docx|.*doc"));

                if (directoryFiles == null)
                {
                    Console.WriteLine("No documents in given directory");
                    return;
                }

                int documentId = 0;
                int wordId = 0;
                foreach (object filePath in directoryFiles)
                {
                    var lastModified = System.IO.File.GetLastWriteTime((string)filePath);
                    context.DocumentMetadata.Add(new DocumentMetadata
                    {
                        DocumentLastEditTime = lastModified,
                        DocumentId = documentId,
                        DocumentAbsolutePath = (string)filePath
                    });
                    context.SaveChanges();
                    MakeIndex(documentId, ref wordId, filePath);
                    documentId++;
                }
            }

        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.Unicode;
            ///string directoryPath = @"C:\Users\Arnolds\Desktop\7semestris\KD Kursa darbs\DSS\DSS\DSS\";
            ///CrawlDirectoryForDocx(directoryPath);
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
