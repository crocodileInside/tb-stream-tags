using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using HtmlAgilityPack;
using Fizzler;
using Fizzler.Systems.HtmlAgilityPack;
using Fizzler.Systems.XmlNodeQuery;
using System.Text.RegularExpressions;

namespace TBStreamTag
{
    class TBParser
    {
        public List<Track> getTracks()
        {
            List<Track> trackList = new List<Track>();
            int i = 0;

            WebClient wc = new WebClient();
            wc.Proxy = null;

            string htmlstr = wc.DownloadString(@"http://www.technobase.fm/tracklist/");
            var html = new HtmlDocument();
            html.LoadHtml(htmlstr);

            var document = html.DocumentNode;

            //Track Name + Artist
            foreach (HtmlNode hn in document.QuerySelectorAll("table.rc_table_detail th"))
            {
                string trackStr = hn.InnerText.Replace("&amp;", "&");
                string[] trackSeparator = new string[] { " - " };
                string[] trackParts = trackStr.Split(trackSeparator, StringSplitOptions.None);

                string trackArtist = trackParts[0];
                string trackTitle = trackParts[1];
                trackList.Add(new Track());
                trackList[i].Title = trackTitle;
                trackList[i].Artist = trackArtist;
                trackList[i].ID = i;

                i++;
            }

            //Track Times
            i = 0;
            foreach (HtmlNode hn in document.QuerySelectorAll("div.rc_release_list_item div.rc_release_list_item_right table.rc_table_detail tr:first-child td"))
            {
                string timeStr = hn.InnerHtml.Substring(0, 5);
                trackList[i].TimeStart = DateTime.Parse(timeStr);
                if (i > 0)
                {
                    trackList[i].TimeEnd = trackList[i - 1].TimeStart;
                }

                i++;
            }

            //Track Cover URL
            Regex coverRegex = new Regex(@"(_)s(\.|_)");
            i = 0;
            foreach (HtmlNode hn in document.QuerySelectorAll("div.rc_release_list_item div.rc_release_list_item_left div.rc_release_list_item_picture img"))
            {
                string imgURL = hn.Attributes["src"].Value;
                trackList[i].CoverURL = coverRegex.Replace(imgURL, @"$1b$2");
                i++;
            }

            return trackList;
        }
    }
}
