using System;
using Sgry.Azuki;
using Path = System.IO.Path;
using IHighlighter = Sgry.Azuki.Highlighter.IHighlighter;
using Highlighters = Sgry.Azuki.Highlighter.Highlighters;

namespace Sgry.Ann
{
	class FileType
	{
		#region Fields & Constants
		public const string TextFileTypeName = "Text";
		public const string LatexFileTypeName = "LaTeX";
		public const string CppFileTypeName = "C/C++";
		public const string CSharpFileTypeName = "C#";
		public const string JavaFileTypeName = "Java";
		public const string RubyFileTypeName = "Ruby";
		public const string XmlFileTypeName = "XML";
		string _Name = null;
		IHighlighter _Highlighter = null;
		AutoIndentHook _AutoIndentHook = null;
		#endregion

		private FileType()
		{}

		#region Factory
		/// <summary>
		/// Gets a new Text file type.
		/// </summary>
		public static FileType TextFileType
		{
			get
			{
				FileType fileType = new FileType();
				fileType._AutoIndentHook = AutoIndentHooks.GenericHook;
				fileType._Name = TextFileTypeName;
				return fileType;
			}
		}

		/// <summary>
		/// Gets a new LaTeX file type.
		/// </summary>
		public static FileType LatexFileType
		{
			get
			{
				FileType fileType = new FileType();
				fileType._Highlighter = Highlighters.Latex;
				fileType._AutoIndentHook = AutoIndentHooks.GenericHook;
				fileType._Name = LatexFileTypeName;
				return fileType;
			}
		}

		/// <summary>
		/// Gets a new C/C++ file type.
		/// </summary>
		public static FileType CppFileType
		{
			get
			{
				FileType fileType = new FileType();
				fileType._Highlighter = Highlighters.Cpp;
				fileType._AutoIndentHook = AutoIndentHooks.CHook;
				fileType._Name = CppFileTypeName;
				return fileType;
			}
		}

		/// <summary>
		/// Gets a new C# file type.
		/// </summary>
		public static FileType CSharpFileType
		{
			get
			{
				FileType fileType = new FileType();
				fileType._Highlighter = Highlighters.CSharp;
				fileType._AutoIndentHook = AutoIndentHooks.CHook;
				fileType._Name = CSharpFileTypeName;
				return fileType;
			}
		}

		/// <summary>
		/// Gets a new Java file type.
		/// </summary>
		public static FileType JavaFileType
		{
			get
			{
				FileType fileType = new FileType();
				fileType._Highlighter = Highlighters.Java;
				fileType._AutoIndentHook = AutoIndentHooks.CHook;
				fileType._Name = JavaFileTypeName;
				return fileType;
			}
		}

		/// <summary>
		/// Gets a new Ruby file type.
		/// </summary>
		public static FileType RubyFileType
		{
			get
			{
				FileType fileType = new FileType();
				fileType._Highlighter = Highlighters.Ruby;
				fileType._AutoIndentHook = AutoIndentHooks.GenericHook;
				fileType._Name = RubyFileTypeName;
				return fileType;
			}
		}

		/// <summary>
		/// Gets a new XML file type.
		/// </summary>
		public static FileType XmlFileType
		{
			get
			{
				FileType fileType = new FileType();
				fileType._Highlighter = Highlighters.Xml;
				fileType._AutoIndentHook = AutoIndentHooks.GenericHook;
				fileType._Name = XmlFileTypeName;
				return fileType;
			}
		}

		public static FileType GetFileTypeByFileName( string fileName )
		{
			string ext;
			string extList;

			ext = Path.GetExtension( fileName );
			if( ext == String.Empty )
			{
				return TextFileType;
			}

			// LaTeX?
			extList = AppConfig.Ini.Get( "LatexFileType", "Extensions", "" );
			if( 0 <= extList.IndexOf(ext, StringComparison.CurrentCultureIgnoreCase) )
			{
				return LatexFileType;
			}

			// C/C++?
			extList = AppConfig.Ini.Get( "CppFileType", "Extensions", "" );
			if( 0 <= extList.IndexOf(ext, StringComparison.CurrentCultureIgnoreCase) )
			{
				return CppFileType;
			}

			// C#?
			extList = AppConfig.Ini.Get( "CSharpFileType", "Extensions", "" );
			if( 0 <= extList.IndexOf(ext, StringComparison.CurrentCultureIgnoreCase) )
			{
				return CSharpFileType;
			}

			// Java?
			extList = AppConfig.Ini.Get( "JavaFileType", "Extensions", "" );
			if( 0 <= extList.IndexOf(ext, StringComparison.CurrentCultureIgnoreCase) )
			{
				return JavaFileType;
			}

			// Ruby?
			extList = AppConfig.Ini.Get( "RubyFileType", "Extensions", "" );
			if( 0 <= extList.IndexOf(ext, StringComparison.CurrentCultureIgnoreCase) )
			{
				return RubyFileType;
			}

			// XML?
			extList = AppConfig.Ini.Get( "XmlFileType", "Extensions", "" );
			if( 0 <= extList.IndexOf(ext, StringComparison.CurrentCultureIgnoreCase) )
			{
				return XmlFileType;
			}

			return TextFileType;
		}
		#endregion

		#region Properties
		/// <summary>
		/// Gets an associated highlighter object.
		/// </summary>
		public IHighlighter Highlighter
		{
			get{ return _Highlighter; }
		}

		/// <summary>
		/// Gets key-hook procedure for Azuki's auto-indent associated with this file-type.
		/// </summary>
		public AutoIndentHook AutoIndentHook
		{
			get{ return _AutoIndentHook; }
		}

		/// <summary>
		/// Gets the name of the file mode.
		/// </summary>
		public String Name
		{
			get{ return _Name; }
		}
		#endregion
	}
}
