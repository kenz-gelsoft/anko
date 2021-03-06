using System;
using System.Text;
using Sgry.Azuki;
using Debug = System.Diagnostics.Debug;
using Regex = System.Text.RegularExpressions.Regex;
using Path = System.IO.Path;

namespace Sgry.Ann
{
	class Document : Azuki.Document
	{
		#region Fields
		string _FilePath = null;
		Encoding _Encoding = Encoding.Default;
		bool _WithBom = false;
		FileType _FileType;
		string _DisplayName = null;
		DateTime _LastSavedTime;
		#endregion

		#region Init / Dispose
		/// <summary>
		/// Creates a new instance.
		/// </summary>
		public Document()
		{
			_FileType = FileType.TextFileType;
			base.MarksUri = true;
			base.WatchPatterns.Register( new WatchPattern(0, null) );
		}
		#endregion

		#region Properties
		/// <summary>
		/// Gets or sets name for display.
		/// </summary>
		public string DisplayName
		{
			get
			{
				if( _FilePath != null )
				{
					return Path.GetFileName( _FilePath );
				}
				else
				{
					return _DisplayName;
				}
			}
			set
			{
				_DisplayName = value;
			}
		}

		/// <summary>
		/// Gets name for display with flags.
		/// </summary>
		public string DisplayNameWithFlags
		{
			get
			{
				if( IsDirty )
					return DisplayName + '*';
				else
					return DisplayName;
			}
		}

		/// <summary>
		/// Gets associated file type object.
		/// </summary>
		public FileType FileType
		{
			get{ return _FileType; }
			set{ _FileType = value; }
		}

		/// <summary>
		/// Gets or sets the file path associated with this document.
		/// </summary>
		public string FilePath
		{
			get{ return _FilePath; }
			set
			{
				// once file path was associated with a document, set display name to the file name and lock it
				_DisplayName = Path.GetFileName( value );
				_FilePath = value;
			}
		}

		/// <summary>
		/// Gets or sets encoding of the document file.
		/// </summary>
		public Encoding Encoding
		{
			get{ return _Encoding; }
			set{ _Encoding = value; }
		}

		/// <summary>
		/// Gets or sets whether a BOM should be used on saving the document.
		/// </summary>
		public bool WithBom
		{
			get{ return _WithBom; }
			set{ _WithBom = value; }
		}

		/// <summary>
		/// Gets or sets time this document was saved.
		/// </summary>
		public DateTime LastSavedTime
		{
			get{ return _LastSavedTime; }
			set{ _LastSavedTime = value; }
		}

		/// <summary>
		/// Gets or sets searching text pattern.
		/// </summary>
		public Regex SearchingPattern
		{
			get{ return base.WatchPatterns.Get(0).Pattern; }
			set{ base.WatchPatterns.Register( new WatchPattern(0, value) ); }
		}
		#endregion

		#region Utilities
		/// <summary>
		/// Gets display name of this document.
		/// </summary>
		public override string ToString()
		{
			return DisplayNameWithFlags;
		}
		#endregion
	}
}
