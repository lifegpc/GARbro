using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using GameRes.Formats.Musica;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;

namespace GameRes.Formats.GUI
{
    /// <summary>
    /// Interaction logic for CreatePAZWidget.xaml
    /// </summary>
    public partial class CreatePAZWidget : Grid
    {
        readonly PazOpener m_paz;

        public CreatePAZWidget (PazOpener paz)
        {
            m_paz = paz;
            InitializeComponent ();
            Title.SelectionChanged += TitleSelectionChanged;
            PopulateTitles ();
            UpdateArchiveList ();
        }

        void PopulateTitles ()
        {
            var titles = new List<string> { arcStrings.ArcNoEncryption };
            titles.AddRange (m_paz.KnownTitles.Keys.OrderBy (x => x));
            Title.ItemsSource = titles;
            string saved_title = Settings.Default.PAZTitle;
            if (!string.IsNullOrEmpty (saved_title) && titles.Contains (saved_title))
                Title.SelectedValue = saved_title;
            else if (titles.Count > 0)
                Title.SelectedIndex = 0;
        }

        void TitleSelectionChanged (object sender, SelectionChangedEventArgs e)
        {
            UpdateArchiveList ();
        }

        void UpdateArchiveList ()
        {
            string title = Title.SelectedValue as string;
            PazScheme scheme = null;
            if (!string.IsNullOrEmpty (title))
                m_paz.KnownTitles.TryGetValue (title, out scheme);

            List<string> arc_keys = (scheme != null && scheme.ArcKeys != null)
                ? scheme.ArcKeys.Keys.OrderBy (x => x).ToList ()
                : new List<string> ();

            Archive.ItemsSource = arc_keys;
            string saved_key = Settings.Default.PAZArchiveKey;
            if (!string.IsNullOrEmpty (saved_key) && arc_keys.Contains (saved_key))
                Archive.SelectedValue = saved_key;
            else if (arc_keys.Count > 0)
                Archive.SelectedIndex = 0;
            else
                Archive.Text = scheme != null ? (saved_key ?? string.Empty) : string.Empty;

            Archive.IsEnabled = scheme != null;
        }

        public string SelectedTitle
        {
            get { return Title.SelectedValue as string ?? Title.Text; }
        }

        public string SelectedArchive
        {
            get
            {
                if (!Archive.IsEnabled)
                    return string.Empty;
                var selected = Archive.SelectedValue as string;
                if (!string.IsNullOrEmpty (selected))
                    return selected;
                return Archive.Text ?? string.Empty;
            }
        }

        public bool CompressContents
        {
            get { return Compress.IsChecked ?? false; }
        }

        public bool RetainStructure
        {
            get { return Retain.IsChecked ?? false; }
        }
    }
}
