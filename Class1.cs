using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Data.OleDb;
using System.Drawing.Imaging;
using System.Drawing;
using ALCRWNS;
using System.Globalization;
using System.Collections.Generic;

namespace PhotoUploader
{
	public abstract class DataEntry
	{
        public DataEntry() { _id = -1; _dumped = false; NeedToDump = false; }

		public int Id 
		{ 
			set { _id = value; }
			get { return _id;  }
		}

		public abstract string GetKey();
		public abstract string GetInsertQuery(int count);

		public static string Escape(string s, char c)
		{
			if (s.IndexOf(c) >= 0)
			{
				StringBuilder sb = new StringBuilder(s);
				sb.Replace("'", c.ToString() + c.ToString());
				return sb.ToString();
			}
			else
			{
				return s;
			}
		}

        public void AddToDataBase()
        {
            if (NeedToDump && !_dumped)
            {
                PhotoDataUploader.ExecuteNonQuery(GetInsertQuery(Id));
                _dumped = true;
            }
        }


		private int _id;
        private bool _dumped;
        public bool NeedToDump;
	}

	public class DataList : Hashtable
	{
		public DataList() {}

		public void Add(DataEntry de)
		{
			string key = de.GetKey();
			if (!ContainsKey(key))
			{
				Add(key, de);
                de.NeedToDump = true;
				de.Id = this.Count;
			}
			else
			{
				de.Id = ((DataEntry)this[key]).Id;
				PhotoDataUploader.Log(0, "Found Item");
			}
		}

	}

	public class Album : DataEntry
	{
		public Album(string name, int month, int year, string photo, string story)
		{
			_name = Escape(name, '\'');
			_month = month;
			_year = year;
            _photo = photo;
            _story = Escape(story, '\'');
		}

		public override string GetKey()
		{
			return _name + _month + _year;
		}

		public override string GetInsertQuery(int count)
		{
			return @"Insert into Albums (AlbumId, AlbumTitle, AlbumYear, AlbumMonth, AlbumPhoto, AlbumHash, AlbumStory) " +
				@"Values(" + count + ", '" + _name + @"'," + _year + @"," + _month + @",'" + _flickrUrl + @"','" + GetAlbumHash() + @"','" + _story + @"')";
		}

        public string GetAlbumHash()
        {
            ulong hash = ((ulong)_year << 4) + (ulong)_month;
            hash <<= 48;

            ulong nh = 0;
            int shift = 0;
            foreach (char ch in _name)
            {
                ulong by = (ulong)Convert.ToUInt16(ch) << shift;
                nh ^= by;
                shift = (shift + 8) % 64;
            }

            nh &= 0x0000ffffffffffff; 
            hash |= nh;

            uint n1 = (uint)(hash & 0x00000000ffffffff);
            uint n2 = (uint)((hash & 0xffffffff00000000) >> 32);
            return Convert.ToString(n2, 16) + Convert.ToString(n1, 16);
        }

		public int Month { get { return _month; } }
		public int Year  { get { return _year;  } }
        public string Photo { get { return _photo; } set { _photo = value; } }
        public string FlickrUrl { get { return _flickrUrl; } set { _flickrUrl = value; } }
        public string Story { get { return _story; } set { _story = value; } }

		private string	_name;
		private int		_month;
		private int		_year;
        private string _photo;

        private string _flickrUrl;
        private string _story;
    }

	public class AlbumList : DataList
	{
		public AlbumList() {}
	}

	public class Place : DataEntry
	{
		public Place(string placeName)
		{
			_placeName = Escape(placeName, '\'');
		}

		public override string GetKey()
		{
			return _placeName;
		}

		public override string GetInsertQuery(int count)
		{
			return @"Insert into Places (PlaceId, PlaceName) " +
				@"Values(" + count + ", '" + _placeName + @"')";
		}

		private string _placeName;
	}

	public class PlaceList : DataList
	{
		public PlaceList() {}
	}

	/// <summary>
	/// Summary description for Class1.
	/// </summary>
	class PhotoDataUploader
	{
		public static AlbumList g_AlbumList;
		public static PlaceList  g_PlaceList;
		public static OleDbConnection g_conn;
		static int _logLevel = 1;
		public static String g_RootPath; /* = @"C:\photos\";*/
		public static String g_DBPath; 

		public static void Log(int level, string message)
		{
			if (level >= _logLevel)
			{
				System.Console.WriteLine(message);
			}
		}

		public static OleDbConnection GetConnection()
		{
			OleDbConnection conn = new OleDbConnection();
			conn.ConnectionString = @"Provider=Microsoft.Jet.OLEDB.4.0; Data source= " + g_DBPath + @"\Photos.mdb";
			conn.Open();
			return conn;
		}

		public static int ExecuteNonQuery(string command)
		{
			int retVal = -1;
			try
			{
				OleDbCommand delCommand = new OleDbCommand(command, g_conn);
				retVal = delCommand.ExecuteNonQuery();
			}
			catch (Exception e)
			{
				Log(3, "Exception: " + e.Message);
				throw(e);
			}
			return retVal;
		}

		static void DeleteFullDatabase()
		{
			int retVal;
			retVal = ExecuteNonQuery("Delete from Photos");
			Log(1, "Deleted " + retVal + " rows from Photos");
			retVal = ExecuteNonQuery("Delete from Albums");
			Log(1, "Deleted " + retVal + " rows from Albums");
			retVal = ExecuteNonQuery("Delete from Places");
			Log(1, "Deleted " + retVal + " rows from Places");
		}

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
			if (args.Length != 2)
			{
				Console.WriteLine("Useage: PhotoUploader photos_src database_dest (no trailing slashes)");
				return;
			}
			else
			{
				g_RootPath = args[0];
				g_DBPath = args[1];
			}

			g_conn = GetConnection();

			try
			{
				Log(3, "Starting");

				Log(2, "Deleting Database");
				DeleteFullDatabase();

				g_AlbumList = new AlbumList();
				g_PlaceList = new PlaceList();

				Log(2, "Reading ABM files and adding to the database.");
				DoRecursiveWalk(g_RootPath);
				Log(3, "Done.");
			}
			finally
			{
				g_conn.Close();
			}
		}

		static void DoRecursiveWalk(string DirPath)
		{
            DirectoryInfo di = new DirectoryInfo(DirPath);
            AbmFileReader afr = new AbmFileReader(di);
            afr.Read(false);
            afr.DumpToDataBase();
			string[] SubDirs = Directory.GetDirectories(DirPath);
			foreach (string SubDir in SubDirs)
			{
				DoRecursiveWalk(SubDir);
			}
		}
	}

	class AbmFileReader : ALCRW
	{
        public AbmFileReader(DirectoryInfo di)
            : base(di)
        {
            _albumsInFolder = new Hashtable();
            _photosInFolder = new List<String>();
        }

        public void DumpToDataBase()
        {
            foreach (DictionaryEntry aif in _albumsInFolder)
            {
                ((Album)(aif.Value)).AddToDataBase();
            }

            foreach (var qS in _photosInFolder)
            {
                PhotoDataUploader.ExecuteNonQuery(qS);
            }
        }

        protected override void AddOneAlbumOverride(string Name, string Month, string Year, string Photo, string Story)
        {
            PhotoDataUploader.Log(1, "Loading Album " + Name + " in " + Month + ", " + Year);
            Album a = new Album(Name, Convert.ToInt32(Month, 10), Convert.ToInt32(Year, 10), Photo, Story);
			PhotoDataUploader.g_AlbumList.Add(a);
			_albumsInFolder.Add(Name, a);
        }

        protected override bool AddOnePhotoOverride(string DateStr, string JustTheName, string Title, string People, string AlbumT, string Place, bool NoShow, bool Favorite, string FlickrId, string FlickrSecret, string FlickrOriginalSecret, string FlickrFarm, string FlickrServer, string Rectangles)
        {
            if (FlickrId == "")
            {
                return false;
            }

            // Add places to the places table
            PhotoDataUploader.g_PlaceList.Add(new Place(Place));

            // Finally add the photo to the list
            string qS = @"Insert into Photos (PhotoId, PhotoTitle, FileName, Path, Favorite, DisableIt, AlbumId, PlaceId, Month_, Date_, Year_, People, FlickrId, FlickrSecret, FlickrOSecret, FlickrFarm, FlickrServer, Rects) Values(";

            g_PhotoCount++;
            qS += g_PhotoCount + ", ";

            //string FullFilename = _abmPath + "\\" + Filename;
            if (Title == "")
            {
                Title = AlbumT;
            }

            // Add in the title
            qS += "'" + DataEntry.Escape(Title, '\'') + "', ";

            // Add in the filename 
            qS += "'" + DataEntry.Escape(JustTheName, '\'') + "', ";

            // Add in the path of the image file
            qS += "'" + DataEntry.Escape("Path", '\'') + "', ";

            // Now the date time string
            //qS += "'" + /*DateTimeStr*/ "10/10/96" + "', ";
            //qS += "TO_DATE('10/10/1996', 'MM/DD/YYY'),";

            // Favorite?
            qS += (Favorite ? "true" : "false") + ", ";

            // DisableIt?
            qS += (NoShow ? "true" : "false") + ", ";

            Album a = (Album)(_albumsInFolder[AlbumT]);
            Place p = (Place)(PhotoDataUploader.g_PlaceList[Place]);
            p.AddToDataBase();

            if (a != null && p != null)
            {
                // AlbumId
                qS += a.Id + ", ";

                // PlaceId
                qS += p.Id + ", ";

                if (a.Photo == "")
                {
                    a.Photo = JustTheName;
                }

                if (a.Photo == JustTheName)
                {
                    const string photourl = "http://farm{0}.static.flickr.com/{1}/{2}_{3}{4}.{5}";
                    a.FlickrUrl = string.Format(photourl, FlickrFarm, FlickrServer, FlickrId, FlickrSecret, "_s", "jpg");
                }
            }
            else
            {
                PhotoDataUploader.Log(3, "Not found either the album or the place - data error");
                return false;
            }

            {
                bool foundDate = false;
                DateTime dt = DateTime.Now;

                // Month, Date, Year
                if (DateStr != "")
                {
                    try
                    {
                        dt = DateTime.Parse(DateStr);
                        foundDate = true;
                    }
                    catch { }

                    if (!foundDate)
                    {
                        try
                        {
                            dt = DateTime.ParseExact(DateStr, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
                            foundDate = true;
                        }
                        catch { }
                    }
                }

                if (foundDate)
                {
                    qS += dt.Month + ", " + dt.Day + ", " + dt.Year;
                }
                else
                {
                    qS += a.Month + ", 1, " + a.Year;
                }
            }

            qS += ", '" + DataEntry.Escape(People, '\'') + "'";

            qS += ", '" + FlickrId + "'";
            qS += ", '" + FlickrSecret + "'";
            qS += ", '" + FlickrOriginalSecret + "'";
            qS += ", '" + FlickrFarm + "'";
            qS += ", '" + FlickrServer + "'";
            qS += ", '" + Rectangles + "'";
            qS += ")";

            _photosInFolder.Add(qS);

            return false;
        }

        protected override bool GetOneAlbumOverride(int i, out string Name, out string Month, out string Year, out string Photo, out string Story)
        {
            throw new NotImplementedException();
        }

        protected override bool GetOnePhotoOverride(int i, out string DateStr, out string JustTheName, out string Title, out string People, out string AlbumT, out string Place, out bool NoShow, out bool Favorite, out string FlickrId, out string FlickrSecret, out string FlickrOriginalSecret, out string FlickrFarm, out string FlickrServer, out string Rectangles)
        {
            throw new NotImplementedException();
        }

		static int g_PhotoCount;
		private Hashtable _albumsInFolder;
        private List<String> _photosInFolder;
	}
}
