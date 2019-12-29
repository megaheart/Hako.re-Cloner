using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Http;
using HtmlAgilityPack;
using System.Net;
using System.Text.Json;
using Fizzler.Systems.HtmlAgilityPack;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Hako.Re_Cloner
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const string SAVE_FOLDER = @"D:\Light novels\";
        string NovelFolder = "";
        string mainUrl = "";
        HttpClient httpClient;
        WebClient webClient;
        List<TreeElement> vols;
        public MainWindow()
        {
            InitializeComponent();
            httpClient = new HttpClient();
            webClient = new WebClient();
            vols = new List<TreeElement>();
            //vols = ListOfChapters.ItemsSource as List<TreeElement>;
            //foreach(var i  in vols)
            //{
            //    foreach(var j in i.Children)
            //    {
            //        j.Parent = i;
            //    }
            //}
        }
        private async void LinkTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox.IsEnabled = false;
            HttpResponseMessage response = null;
            try
            {
                response = await httpClient.GetAsync(textBox.Text);
            }
            catch(Exception)
            {
                textBox.IsEnabled = true;
                return;
            }
            if (response.IsSuccessStatusCode)
            {
                mainUrl = textBox.Text;
                try
                {
                    await LoadVols(await response.Content.ReadAsStringAsync());
                }
                catch (Exception exc)
                {
                    MessageBox.Show(exc.ToString());
                    textBox.IsEnabled = true;
                    return;
                }
                ListOfChapters.ItemsSource = vols;
                LNViewer.Visibility = Visibility.Visible;
            }
            else // get html unsuccessfully
            {
                textBox.IsEnabled = true;
            }
        }
        private async Task LoadVols(string html)
        {
            HtmlDocument htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);
            var sections = htmlDocument.DocumentNode.QuerySelectorAll(".volume-list");
            foreach(var s in sections)
            {
                TreeElement vol = new TreeElement();
                vol.Children = new List<TreeElement>();
                vol.Name = s.QuerySelector("header>.sect-title>a").InnerHtml;
                vol.IsChecked = false;
                var chapters = s.QuerySelectorAll(".list-chapters>li>div>a");
                foreach(var a in chapters)
                {
                    TreeElement chapter = new TreeElement();
                    chapter.Name = a.InnerText;
                    chapter.url = a.Attributes["href"].Value;
                    chapter.code = CodeOfChapter(chapter.url);
                    chapter.IsChecked = false;
                    vol.Children.Add(chapter);
                    chapter.Parent = vol;
                }
                vols.Add(vol);
            }
            NovelFolder = ForceTrim(htmlDocument.DocumentNode.QuerySelector(".series-name").InnerText) + "\\";
            if (File.Exists(SAVE_FOLDER + NovelFolder + "manifest.json"))
            {
                var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(SAVE_FOLDER + NovelFolder + "manifest.json"));
                int index = 0;
                foreach(var i in manifest.RootElement.EnumerateArray())
                {
                    int numberOfNotDownloaded = vols[index].Children.Count;
                    foreach (var j in vols[index].Children)
                    {
                        if(i.EnumerateArray().Any(x=>x.GetProperty("code").ToString() == j.code))
                        {
                            j.IsNotDownloaded = false;
                            j.IsChecked = true;
                            numberOfNotDownloaded--;
                        }
                    }
                    vols[index].IsNotDownloaded = numberOfNotDownloaded != 0;
                    vols[index].IsChecked = numberOfNotDownloaded == 0;
                    index++;
                }
            }
        }
        bool IsVol_CheckedLocked = false;
        private async void Vol_Checked(object sender, RoutedEventArgs e)
        {
            if (IsVol_CheckedLocked) return;
            IsChap_CheckedLocked = true;
            CheckBox checkBox = sender as CheckBox;
            TreeElement treeElement = checkBox.DataContext as TreeElement;
            foreach(var i in treeElement.Children)
            {
                if (i.IsNotDownloaded)
                {
                    i.IsChecked = checkBox.IsChecked.Value;
                }
            }
            IsChap_CheckedLocked = false;
        }
        bool IsChap_CheckedLocked = false;
        private async void Chap_Checked(object sender, RoutedEventArgs e)
        {
            if (IsChap_CheckedLocked) return;
            IsVol_CheckedLocked = true;
            CheckBox checkBox = sender as CheckBox;
            TreeElement treeElement = checkBox.DataContext as TreeElement;
            if (checkBox.IsChecked.Value)
            {
                treeElement.Parent.IsChecked = true;
                foreach (var i in treeElement.Parent.Children)
                {
                    if (i.IsChecked == false)
                    {
                        treeElement.Parent.IsChecked = false;
                        break;
                    }
                }
            }
            else
            {
                treeElement.Parent.IsChecked = false;
            }
            IsVol_CheckedLocked = false;
        }
        private async void Download(object sender, RoutedEventArgs e)
        {
            DownloadBtn.IsEnabled = false;
            ListOfChapters.IsEnabled = false;
            List<List<ChapterInfo>> manefest = new List<List<ChapterInfo>>();
            List<string> codes = new List<string>();
            foreach (var i in vols)
            {
                List<ChapterInfo> chapters = new List<ChapterInfo>();
                int numberOfNotDownloaded = i.Children.Count;
                foreach (var j in i.Children)
                {
                    if(j.IsChecked && j.IsNotDownloaded)
                    {
                        codes.Add(j.code);
                        j.IsNotDownloaded = false;
                    }
                    if (j.IsChecked)
                    {
                        chapters.Add(new ChapterInfo() { code = j.code, name = j.Name });
                        numberOfNotDownloaded--;
                    }
                }
                i.IsNotDownloaded = numberOfNotDownloaded != 0;
                manefest.Add(chapters);
            }
            ProgressViewer.Visibility = Visibility.Visible;
            TheProgressBar.Value = 0;
            TheProgressBar.Minimum = 0;
            TheProgressBar.Maximum = 1;
            int process = 0;
            while (codes.Count > process)
            {
                string url = mainUrl + @"/c" + codes[process];
                ProgressMessage.Text = "downloading " + url;
                await DownloadChapter(url, codes[process]);
                process = process + 1;
                TheProgressBar.Value = (double)process / codes.Count;
            }
            await File.WriteAllTextAsync(SAVE_FOLDER + NovelFolder + "manifest.json", JsonSerializer.Serialize(manefest));
            ProgressViewer.Visibility = Visibility.Collapsed;
            DownloadBtn.IsEnabled = true;
            ListOfChapters.IsEnabled = true;
        }
        static string ForceTrim(string s)
        {
            Regex regex = new Regex(@"\w+(\s\w+)*");
            Match match = regex.Match(s);
            return match.Value;
        }
        private string CodeOfChapter(string url)
        {
            Regex regex = new Regex(@"/c[0-9]*");
            Match match = regex.Match(url);
            if (match.Success)
            {
                return match.Value.Substring(2);
            }
            throw new Exception("Code Of Chapter In Hako.re Is Changed");
        }
        private async Task<string> httpClient_GetAsync(string url)
        {
            HttpResponseMessage response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(5000);
                    return await httpClient_GetAsync(url);
                }
                else throw new Exception("DownloadChapter other errors");
            }
            return await response.Content.ReadAsStringAsync();
        }
        private async Task DownloadChapter(string url, string chapterName)
        {
            if(!Directory.Exists(SAVE_FOLDER + NovelFolder))
            {
                Directory.CreateDirectory(SAVE_FOLDER + NovelFolder);
            }
            string saveString = "";
            HtmlDocument htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(await httpClient_GetAsync(url));
            var paragraphs = htmlDocument.DocumentNode.QuerySelectorAll("#chapter-content>p");
            foreach(var p in paragraphs)
            {
                var imgs = p.QuerySelectorAll("img");
                foreach (var img in imgs)
                {
                    img.Attributes["src"].Value = await DownloadImage(img.Attributes["src"].Value);
                }
                saveString += p.OuterHtml + "\n";
            }
            await File.WriteAllTextAsync(SAVE_FOLDER + NovelFolder + chapterName, saveString).ConfigureAwait(false);
        }
        private async Task<string> DownloadImage(string url)
        {
            string output = DateTime.Now.Ticks + ExtensionOfImage(url);
            await webClient.DownloadFileTaskAsync(new Uri(url), SAVE_FOLDER + NovelFolder + output);
            return output;
        }
        private string ExtensionOfImage(string url)
        {
            string output = "";
            int i = url.Length - 1;
            while (i > -1)
            {
                if (url[i] == '.') return '.' + output;
                else output = url[i] + output;
                i--;
            }
            return output;
        }
    }
    public class ChapterInfo
    {
        public string name { set; get; }
        public string code { set; get; }
    }
    public class TreeElement: INotifyPropertyChanged
    {
        public string Name { set; get; }
        public string url;
        public string code;
        public TreeElement Parent { set; get; }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
        private bool _isChecked;
        public bool IsChecked { 
            set { 
                if(_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged("IsChecked");
                }
            }
            get => _isChecked;
        }
        private bool _isNotDownloaded = true;
        public bool IsNotDownloaded
        {
            set
            {
                if (_isNotDownloaded != value)
                {
                    _isNotDownloaded = value;
                    OnPropertyChanged("IsNotDownloaded");
                }
            }
            get => _isNotDownloaded;
        }
        public List<TreeElement> Children { set; get; }
    }
    //public class DataTreeViewTest : List<TreeElement>
    //{
    //    public DataTreeViewTest():base(new TreeElement[] {
    //    new TreeElement() { Name = "v1", Children = new List<TreeElement>(){ 
    //        new TreeElement(){Name = "v1" + "-child_1"},
    //        new TreeElement() { Name = "v1" + "-child_2" },
    //        new TreeElement() { Name = "v1" + "-child_3" },
    //        new TreeElement() { Name = "v1" + "-child_4" },
    //        new TreeElement() { Name = "v1" + "-child_5" }
    //    } },
    //    new TreeElement() { Name = "v1", Children = new List<TreeElement>(){
    //        new TreeElement(){Name = "v1" + "-child_1"},
    //        new TreeElement() { Name = "v1" + "-child_2" },
    //        new TreeElement() { Name = "v1" + "-child_3" },
    //        new TreeElement() { Name = "v1" + "-child_4" },
    //        new TreeElement() { Name = "v1" + "-child_5" }
    //    } },
    //    new TreeElement() { Name = "v1", Children = new List<TreeElement>(){
    //        new TreeElement(){Name = "v1" + "-child_1"},
    //        new TreeElement() { Name = "v1" + "-child_2" },
    //        new TreeElement() { Name = "v1" + "-child_3" },
    //        new TreeElement() { Name = "v1" + "-child_4" },
    //        new TreeElement() { Name = "v1" + "-child_5" }
    //    } },
    //    new TreeElement() { Name = "v1", Children = new List<TreeElement>(){
    //        new TreeElement(){Name = "v1" + "-child_1"},
    //        new TreeElement() { Name = "v1" + "-child_2" },
    //        new TreeElement() { Name = "v1" + "-child_3" },
    //        new TreeElement() { Name = "v1" + "-child_4" },
    //        new TreeElement() { Name = "v1" + "-child_5" }
    //    } },
    //    new TreeElement() { Name = "v1", Children = new List<TreeElement>(){
    //        new TreeElement(){Name = "v1" + "-child_1"},
    //        new TreeElement() { Name = "v1" + "-child_2" },
    //        new TreeElement() { Name = "v1" + "-child_3" },
    //        new TreeElement() { Name = "v1" + "-child_4" },
    //        new TreeElement() { Name = "v1" + "-child_5" }
    //    } }

    //    })
    //    {
            
    //    }
    //}
}
