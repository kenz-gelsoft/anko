// file: Document.cs
// brief: Document of Azuki engine.
//=========================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Debug = System.Diagnostics.Debug;

namespace Sgry.Azuki
{
	using Highlighter;

	/// <summary>
	/// The document of the Azuki editor engine.
	/// </summary>
	public class Document : IEnumerable<char>
	{
		#region Fields
		static int _InstanceCounter;
		readonly int _InstanceCount;
		readonly TextBuffer _Buffer;
		readonly EditHistory _History = new EditHistory();
		readonly SelectionManager _SelMan;
		bool _IsRecordingHistory = true;
		bool _IsSuppressingDirtyStateChangedEvent = false;
		readonly WatchPatternSet _WatchPatterns = new WatchPatternSet();
		readonly WatchPatternMarker _WatchPatternMarker;
		readonly LineRangeList _LineRangeList;
		readonly RawLineRangeList _RawLineRangeList;
		string _EolCode = "\r\n";
		IHighlighter _Highlighter = null;
		IWordProc _WordProc = new DefaultWordProc();
		static readonly char[] _PairBracketTable = new char[]{
			'(', ')', '{', '}', '[', ']', '<', '>',
			'\xff08', '\xff09', // full-width parenthesis
			'\xff5b', '\xff5d', // full-width curly bracket
			'\xff3b', '\xff3d', // full-width square bracket
			'\xff1c', '\xff1e', // full-width less/greater than sign
			'\x3008', '\x3009', // CJK angle bracket
			'\x300a', '\x300b', // CJK double angle bracket
			'\x300c', '\x300d', // CJK corner bracket
			'\x300e', '\x300f', // CJK white corner bracket
			'\x3010', '\x3011', // CJK black lenticular bracket
			'\x3016', '\x3017', // CJK white lenticular bracket
			'\x3014', '\x3015', // CJK tortoise shell bracket
			'\x3018', '\x3019', // CJK white tortoise shell bracket
			'\x301a', '\x301b', // CJK white square bracket
			'\xff62', '\xff63' // half-width CJK corner bracket
		};
		#endregion

		#region Init / Dispose
		/// <summary>
		/// Creates a new instance.
		/// </summary>
		public Document()
		{
			MarksUri = false;
			_Buffer = new TextBuffer( this, 4096, 1024 );
			_SelMan = new SelectionManager( this );
			_WatchPatternMarker = new WatchPatternMarker( this );
			_LineRangeList = new LineRangeList( this );
			_RawLineRangeList = new RawLineRangeList( this );
			if( unchecked(++_InstanceCounter) != 0 )
				_InstanceCounter = unchecked(_InstanceCounter + 1);
			_InstanceCount = _InstanceCounter;
		}

		///	<summary>
		/// Gets hash code of this document.
		///	</summary>
		public override int GetHashCode()
		{
			return _InstanceCount;
		}
		#endregion

		#region States
		/// <summary>
		/// Gets or sets whether any unsaved modifications exist or not.
		/// </summary>
		/// <remarks>
		///   <para>
		///   This property will be true if there is any unsaved modifications. Azuki never change
		///   this property value to False by itself so applications must set this property to
		///   False manually on saving document content.
		///   </para>
		/// </remarks>
		/// <exception cref="InvalidOperationException">
		///   True was set as a new value.
		///   - OR -
		///   Modified while grouping UNDO actions.
		/// </exception>
		public bool IsDirty
		{
			get{ return !_History.IsSavedState; }
			set
			{
				if( value )
					throw new InvalidOperationException( "Document.IsDirty must not be set True by"
														 + " application code." );

				if( _History.IsGroupingActions )
					throw new InvalidOperationException( "Dirty state must not be modified while"
														 + " grouping UNDO actions." );

				if( _History.IsSavedState != value )
					return;

				// clean up dirty state of all modified lines
				for( int i=0; i<Lines.Count; i++ )
					if( Lines[i].DirtyState == DirtyState.Dirty )
						Lines[i].DirtyState = DirtyState.Saved;

				// remember current state as lastly saved state
				_History.SetSavedState();

				// invoke event
				InvokeDirtyStateChanged();
			}
		}

		/// <summary>
		/// Gets or sets whether this document is recording edit actions or not.
		/// </summary>
		public bool IsRecordingHistory
		{
			get{ return _IsRecordingHistory; }
			set{ _IsRecordingHistory = value; }
		}

		/// <summary>
		/// Gets or sets whether this document is read-only or not.
		/// </summary>
		/// <remarks>
		/// While this property is true, no modification can be done through user interface. Note
		/// that documents still can be modified programatically through API.
		/// </remarks>
		public bool IsReadOnly
		{
			get; set;
		}

		/// <summary>
		/// Gets whether an available UNDO action exists or not.
		/// </summary>
		/// <remarks>
		///   <para>
		///   This property gets whether one or more UNDOable action exists or not.
		///   </para>
		///   <para>
		///   To execute UNDO, use <see cref="Document.Undo"/> method.
		///   </para>
		/// </remarks>
		/// <seealso cref="Document.Undo"/>
		public bool CanUndo
		{
			get{ return _History.CanUndo; }
		}

		/// <summary>
		/// Gets whether an available REDO action exists or not.
		/// </summary>
		public bool CanRedo
		{
			get{ return _History.CanRedo; }
		}

		/// <summary>
		/// Gets or sets the size of the internal buffer.
		/// </summary>
		/// <exception cref="OutOfMemoryException"/>
		public int Capacity
		{
			get{ return _Buffer.Capacity; }
			set{ _Buffer.Capacity = value; }
		}

		/// <summary>
		/// Gets the time when this document was last modified.
		/// </summary>
		public DateTime LastModifiedTime
		{
			get{ return _Buffer.LastModifiedTime; }
		}

		/// <summary>
		/// Gets the list of watching patterns.
		/// </summary>
		/// <seealso cref="WatchPattern"/>
		/// <seealso cref="WatchPatternSet"/>
		public WatchPatternSet WatchPatterns
		{
			get{ return _WatchPatterns; }
		}
		#endregion

		#region Selection
		/// <summary>
		/// Gets index of where the caret is at (in char-index).
		/// </summary>
		/// <remarks>
		///   <para>
		///   This property gets the index of the 'caret;' the text insertion point.
		///   </para>
		///   <para>
		///   In Azuki, selection always exists and is expressed by the range from anchor index to caret index.
		///   If there is nothing selected, it means that both anchor index and caret index is set to same value.
		///   </para>
		///   <para>
		///   To set value of anchor or caret, use
		///   <see cref="Document.SetSelection(int, int)"/> method.
		///   </para>
		/// </remarks>
		/// <seealso cref="Document.AnchorIndex"/>
		/// <seealso cref="Document.SetSelection(int, int)"/>
		public int CaretIndex
		{
			get
			{
				Debug.Assert( _SelMan.CaretIndex <= Length );
				return _SelMan.CaretIndex;
			}
		}

		/// <summary>
		/// Gets index of the position where the selection starts (in char-index).
		/// </summary>
		/// <remarks>
		///   <para>
		///   This property gets the index of the 'selection anchor;' where the selection starts.
		///   </para>
		///   <para>
		///   In Azuki, selection always exists and is expressed by the range from anchor index to caret index.
		///   If there is nothing selected, it means that both anchor index and caret index is set to same value.
		///   </para>
		///   <para>
		///   To set value of anchor or caret, use
		///   <see cref="Document.SetSelection(int, int)"/> method.
		///   </para>
		/// </remarks>
		/// <seealso cref="Document.CaretIndex"/>
		/// <seealso cref="Document.SetSelection(int, int)"/>
		public int AnchorIndex
		{
			get
			{
				Debug.Assert( _SelMan.AnchorIndex <= Length );
				return _SelMan.AnchorIndex;
			}
		}

		/// <summary>
		/// Gets selection manager object associated with this document.
		/// </summary>
		internal SelectionManager SelectionManager
		{
			get{ return _SelMan; }
		}

		/// <summary>
		/// Gets caret location by logical line/column index.
		/// </summary>
		/// <param name="lineIndex">line index of where the caret is at</param>
		/// <param name="columnIndex">column index of where the caret is at</param>
		public void GetCaretIndex( out int lineIndex, out int columnIndex )
		{
			var pos = GetLineColumnPos( _SelMan.CaretIndex );
			lineIndex = pos.Line;
			columnIndex = pos.Column;
		}

		/// <summary>
		/// Sets caret location by logical line/column index.
		/// Note that calling this method will release selection.
		/// </summary>
		/// <param name="lineIndex">new line index of where the caret is at</param>
		/// <param name="columnIndex">new column index of where the caret is at</param>
		/// <exception cref="ArgumentOutOfRangeException">Specified index is out of valid range.</exception>
		public void SetCaretIndex( int lineIndex, int columnIndex )
		{
			if( lineIndex < 0 )
				throw new ArgumentOutOfRangeException( "lineIndex", "lineIndex must not be a"
													   + " negative value." );
			if( columnIndex < 0 )
				throw new ArgumentOutOfRangeException( "columnIndex", "columnIndex must not be a"
													   + " negative value." );

			int caretIndex = _Buffer.GetCharIndex( new LineColumnPos(lineIndex, columnIndex) );
			SetSelection( caretIndex, caretIndex );
		}

		/// <summary>
		/// Sets selection range.
		/// </summary>
		/// <param name="anchor">new index of the selection anchor</param>
		/// <param name="caret">new index of the caret</param>
		/// <exception cref="System.ArgumentOutOfRangeException">Specified index is out of valid range.</exception>
		/// <remarks>
		///   <para>
		///   This method sets selection range and invokes
		///   <see cref="Document.SelectionChanged">Document.SelectionChanged</see> event.
		///   If given index is at middle of an undividable character sequence such as surrogate pair,
		///   selection range will be automatically expanded to avoid dividing the it.
		///   </para>
		///   <para>
		///   This method always selects text as a sequence of character.
		///   To select text by lines or by rectangle, use
		///   <see cref="Document.SetSelection(int, int, IView)">other overload</see>
		///   method instead.
		///   </para>
		/// </remarks>
		/// <seealso cref="Document.SelectionChanged"/>
		/// <seealso cref="Document.SetSelection(int, int, IView)"/>
		public void SetSelection( int anchor, int caret )
		{
			SelectionMode = TextDataType.Normal;
			SetSelection( anchor, caret, null );
		}

		/// <summary>
		/// Sets selection range.
		/// </summary>
		/// <param name="anchor">new index of the selection anchor.</param>
		/// <param name="caret">new index of the caret.</param>
		/// <param name="view">a View object to be used for calculating position/index conversion.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">Specified index is out of valid range.</exception>
		/// <exception cref="System.ArgumentNullException">Parameter 'view' is null but current SelectionMode is not TextDataType.Normal.</exception>
		/// <remarks>
		///   <para>
		///   This method sets selection range and invokes
		///   <see cref="Document.SelectionChanged">Document.SelectionChanged</see> event.
		///   </para>
		///   <para>
		///   How text will be selected depends on the value of current
		///   <see cref="Document.SelectionMode">SelectionMode</see> as below.
		///   </para>
		///   <list type="bullet">
		///	    <item>
		///	      <para>
		///	      If SelectionMode is TextDataType.Normal,
		///	      characters from <paramref name="anchor"/> to <paramref name="caret"/>
		///	      will be selected.
		///	      </para>
		///	      <para>
		///	      Note that if given index is at middle of an undividable character sequence such as surrogate pair,
		///	      selection range will be automatically expanded to avoid dividing it.
		///	      </para>
		///	    </item>
		///	    <item>
		///	      <para>
		///	      If SelectionMode is TextDataType.Line, lines between
		///	      the line containing <paramref name="anchor"/> position
		///	      and the line containing <paramref name="caret"/> position
		///	      will be selected.
		///	      </para>
		///	      <para>
		///	      Note that if caret is just at beginning of a line,
		///	      the line will not be selected.
		///	      </para>
		///	    </item>
		///	    <item>
		///	      <para>
		///	      If SelectionMode is TextDataType.Rectangle,
		///	      text covered by the rectangle which is graphically made by
		///	      <paramref name="anchor"/> position and <paramref name="caret"/> position
		///	      will be selected.
		///	      </para>
		///	    </item>
		///   </list>
		/// </remarks>
		/// <seealso cref="Document.SelectionChanged"/>
		/// <seealso cref="Document.SelectionMode"/>
		/// <seealso cref="TextDataType"/>
		public void SetSelection( int anchor, int caret, IView view )
		{
			if( anchor < 0 || _Buffer.Count < anchor )
				throw new ArgumentOutOfRangeException( "anchor", "Parameter 'anchor' is out of valid range (anchor:"+anchor+", caret:"+caret+")." );
			if( caret < 0 || _Buffer.Count < caret )
				throw new ArgumentOutOfRangeException( "caret", "Parameter 'caret' is out of valid range (anchor:"+anchor+", caret:"+caret+")." );
			if( view == null && SelectionMode != TextDataType.Normal )
				throw new ArgumentNullException( "view", "Parameter 'view' must not be null if SelectionMode is not TextDataType.Normal. (SelectionMode:"+SelectionMode+")." );

			_SelMan.SetSelection( anchor, caret, (IViewInternal)view );
		}

		/// <summary>
		/// Gets range of currently selected text. Note that this method does not return [anchor,
		/// caret) but [begin, end).
		/// </summary>
		public IRange GetSelection()
		{
			return _SelMan.GetSelection();
		}

		/// <summary>
		/// Gets range of currently selected text. Note that this method does not return [anchor,
		/// caret) pair but [begin, end) pair.
		/// </summary>
		/// <param name="begin">index of where the selection begins.</param>
		/// <param name="end">index of where the selection ends (selection do not includes the char at this index).</param>
		public void GetSelection( out int begin, out int end )
		{
			var range = GetSelection();
			begin = range.Begin;
			end = range.End;
		}

		/// <summary>
		/// Gets or sets text ranges selected by rectangle selection.
		/// </summary>
		/// <remarks>
		///   <para>
		///   (This property is basically for internal use only.
		///   Using this method from outside of Azuki assembly is not recommended.)
		///   </para>
		///   <para>
		///   The value of this method is an array of text indexes
		///   that is consisted with beginning index of first text range (row),
		///   ending index of first text range,
		///   beginning index of second text range,
		///   ending index of second text range and so on.
		///   </para>
		/// </remarks>
		public int[] RectSelectRanges
		{
			get{ return _SelMan.RectSelectRanges; }
			set{ _SelMan.RectSelectRanges = value; }
		}
		#endregion

		#region Content Access
		internal TextBuffer Buffer
		{
			get{ return _Buffer; }
		}

		/// <summary>
		/// Gets collection of line ranges, which does not include EOL code.
		/// </summary>
		/// <seealso cref="Document.RawLines"/>
		public ILineRangeList Lines
		{
			get{ return _LineRangeList; }
		}

		/// <summary>
		/// Gets collection of line ranges, which includes EOL code.
		/// </summary>
		/// <seealso cref="Document.Lines"/>
		public ILineRangeList RawLines
		{
			get{ return _RawLineRangeList; }
		}

		/// <summary>
		/// Gets or sets currently inputted text.
		/// </summary>
		/// <remarks>
		///   <para>
		///   Getting text content through this property
		///   will copy all characters from internal buffer
		///   to a string object and returns it.
		///   </para>
		/// </remarks>
		public string Text
		{
			get
			{
				if( _Buffer.Count == 0 )
					return String.Empty;

				return _Buffer.GetText( new Range(0, _Buffer.Count) );
			}
			set
			{
				if( value == null )
					value = String.Empty;

				Replace( value, 0, Length );
				SetSelection( 0, 0 );
			}
		}

		/// <summary>
		/// Gets a range of a word at specified index.
		/// </summary>
		/// <param name="index">The range of a word at this index will be retrieved.</param>
		/// <exception cref="ArgumentOutOfRangeException">Specified index is out of valid range.</exception>
		public IRange GetWordRange( int index )
		{
			if( index < 0 || _Buffer.Count < index ) // index can be equal to char-count
				throw new ArgumentOutOfRangeException( "index", "Invalid index was given (index:"+index+", this.Length:"+Length+")." );

			var range = new Range( index, index );

			// if specified position indicates an empty line, select nothing
			if( IsEmptyLine(index) )
			{
				return range;
			}

			// ask word processor where the word starting/ending positions are
			range.Begin = WordProc.PrevWordStart( this, index );
			range.End = WordProc.NextWordEnd( this, index );
			if( range.IsEmpty )
			{
				if( Length <= range.End || Char.IsWhiteSpace(this[range.End]) )
				{
					range.Begin = (0 <= index-1) ? WordProc.PrevWordStart( this, index-1 )
												 : 0;
				}
				else
				{
					range.End = (index+1 < Length) ? WordProc.NextWordEnd( this, index+1 )
												   : Length;
				}
			}
			Debug.Assert( 0 <= range.Begin );
			Debug.Assert( 0 <= range.End );
			Debug.Assert( range.Begin <= range.End );

			return range;
		}

		/// <summary>
		/// Gets the number of characters in this document.
		/// </summary>
		/// <remarks>
		///   <para>
		///   This property returns the number of characters in this document. Note that because
		///   Azuki's internal character encoding is UTF-16, characters consisted with more than
		///   one characters will NOT be counted as one (e.g. surrogate pairs, combining character
		///   sequences, and variation sequences.)
		///   </para>
		/// </remarks>
		public int Length
		{
			get{ return _Buffer.Count; }
		}

		/// <summary>
		/// Gets content in this document.
		/// </summary>
		public string GetText()
		{
			return _Buffer.GetText();
		}

		/// <summary>
		/// Gets a portion of text in a specified range.
		/// </summary>
		/// <remarks>
		///   <para>
		///   If given range covers a part of an undividable character sequence such as surrogate
		///   pairs, the range will be automatically expanded so that they will not be divided.
		///   </para>
		/// </remarks>
		/// <exception cref="ArgumentOutOfRangeException"/>
		public string GetText( int begin, int end )
		{
			return GetText( new Range(begin, end) );
		}

		/// <summary>
		/// Gets a portion of text in a specified range.
		/// </summary>
		/// <remarks>
		///   <para>
		///   If given range covers a part of an undividable character sequence such as surrogate
		///   pairs, the range will be automatically expanded so that they will not be divided.
		///   </para>
		/// </remarks>
		/// <exception cref="ArgumentOutOfRangeException"/>
		public string GetText( IRange range )
		{
			return _Buffer.GetText( range );
		}

		/// <summary>
		/// Gets a portion of text in a specified range.
		/// </summary>
		/// <remarks>
		///   <para>
		///   If given range covers a part of an undividable character sequence such as surrogate
		///   pairs, the range will be automatically expanded so that they will not be divided.
		///   </para>
		/// </remarks>
		/// <exception cref="ArgumentOutOfRangeException"/>
		public string GetText( int beginLineIndex, int beginColumnIndex,
							   int endLineIndex, int endColumnIndex )
		{
			return _Buffer.GetText( beginLineIndex, beginColumnIndex,
									endLineIndex, endColumnIndex );
		}

		/// <summary>
		/// Gets a portion of text in a specified range.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException"/>
		public string GetText( LineColumnPos beginPos, LineColumnPos endPos )
		{
			return _Buffer.GetText( beginPos.Line, beginPos.Column, endPos.Line, endPos.Column );
		}

		/// <summary>
		/// Gets class of the character at given index.
		/// </summary>
		/// <param name="index">The index of character which class is to be determined.</param>
		/// <returns>The class of the character at specified index.</returns>
		/// <exception cref="ArgumentOutOfRangeException">Specified index is out of valid range.</exception>
		public CharClass GetCharClass( int index )
		{
			if( Length <= index )
				throw new ArgumentOutOfRangeException( "index", "Invalid index was given (index:"+index+", Length:"+Length+")." );

			return _Buffer.GetCharClassAt( index );
		}

		/// <summary>
		/// Sets class of the character at given index.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException">Specified index is out of valid range.</exception>
		public void SetCharClass( int index, CharClass klass )
		{
			if( Length <= index )
				throw new ArgumentOutOfRangeException( "index", "Invalid index was given (index:"+index+", Length:"+Length+")." );

			_Buffer.SetCharClassAt( index, klass );
		}

		/// <summary>
		/// Replaces current selection.
		/// </summary>
		/// <exception cref="ArgumentNullException">Parameter text is null.</exception>
		public void Replace( string text )
		{
			int begin, end;

			GetSelection( out begin, out end );

			Replace( text, begin, end );
		}

		/// <summary>
		/// Replaces specified range [begin, end) of the content into the given string.
		/// </summary>
		/// <param name="text">specified range will be replaced with this text</param>
		/// <param name="begin">begin index of the range to be replaced</param>
		/// <param name="end">end index of the range to be replaced</param>
		/// <exception cref="ArgumentNullException">Parameter text is null.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Specified index is out of valid range.</exception>
		public void Replace( string text, int begin, int end )
		{
			if( begin < 0 || _Buffer.Count < begin )
				throw new ArgumentOutOfRangeException( "begin", "Invalid index was given (begin:"+begin+", this.Length:"+Length+")." );
			if( end < begin || _Buffer.Count < end )
				throw new ArgumentOutOfRangeException( "end", "Invalid index was given (begin:"+begin+", end:"+end+", this.Length:"+Length+")." );
			if( text == null )
				throw new ArgumentNullException( "text" );

			string oldText = String.Empty;
			LineDirtyStateUndoInfo ldsUndoInfo = null;

			// Do nothing if the operation has no effect
			if( text == "" && begin == end )
				return;

			// first of all, remember current dirty state of the lines
			// which will be modified by this replacement
			var wasSavedState = _History.IsSavedState;
			if( _IsRecordingHistory )
			{
				ldsUndoInfo = new LineDirtyStateUndoInfo();

				// calculate range of the lines which will be affectd by this replacement
				var affectedBeginLI = Lines.AtOffset( begin ).LineIndex;
				if( 0 < begin-1 && _Buffer[begin-1] == '\r' )
				{
					if( (0 < text.Length && text[0] == '\n')
						|| (text.Length == 0 && end < _Buffer.Count && _Buffer[end] == '\n') )
					{
						// A new CR+LF will be created by this replacement
						// so one previous line will also be affected.
						affectedBeginLI--;
					}
				}
				int affectedLineCount = Lines.AtOffset( end ).LineIndex - affectedBeginLI + 1;
				Debug.Assert( 0 < affectedLineCount );

				// store current state of the lines as 'deleted' history
				ldsUndoInfo.LineIndex = affectedBeginLI;
				ldsUndoInfo.DeletedStates = new DirtyState[ affectedLineCount ];
				for( int i=0; i<affectedLineCount; i++ )
				{
					ldsUndoInfo.DeletedStates[i] = _Buffer.GetLineRange( affectedBeginLI + i )
														  .DirtyState;
				}
			}

			// keep copy of the part which will be deleted by this replacement
			if( begin < end )
			{
				var buf = new StringBuilder( end - begin );
				for( int i=begin; i<end; i++ )
					buf.Append( _Buffer[i] );
				oldText = buf.ToString();
			}

			// keep copy of old caret/anchor index
			var oldAnchor = AnchorIndex;
			var oldCaret = CaretIndex;
			var newAnchor = AnchorIndex;
			var newCaret = CaretIndex;

			// delete target range
			if( begin < end )
			{
				// manage line head indexes and delete content
				_Buffer.Remove( begin, end );

				// manage caret/anchor index
				if( begin < newCaret )
				{
					newCaret -= end - begin;
					if( newCaret < begin )
						newCaret = begin;
				}
				if( begin < newAnchor )
				{
					newAnchor -= end - begin;
					if( newAnchor < begin )
						newAnchor = begin;
				}
			}

			// then, insert text
			if( 0 < text.Length )
			{
				// manage line head indexes and insert content
				_Buffer.Insert( begin, text );

				// manage caret/anchor index
				if( begin <= newCaret )
				{
					newCaret += text.Length;
					if( _Buffer.Count < newCaret ) // this is not "end" but "_Buffer.Count"
						newCaret = _Buffer.Count;
				}
				if( begin <= newAnchor )
				{
					newAnchor += text.Length;
					if( _Buffer.Count < newAnchor )
						newAnchor = _Buffer.Count;
				}
			}

			// calc diff of anchor/caret between old and new positions
			var anchorDelta = newAnchor - oldAnchor;
			var caretDelta = newCaret - oldCaret;

			// stack UNDO history
			if( _IsRecordingHistory )
			{
				var undo = new EditAction( this, begin, oldText, text,
										   oldAnchor, oldCaret, ldsUndoInfo );
				_History.Add( undo );
			}

			// convert anchor/caret index in current text
			oldAnchor += anchorDelta;
			oldCaret += caretDelta;

			// update selection
			_SelMan.AnchorIndex = newAnchor;
			_SelMan.CaretIndex = newCaret;

			// examine post assertions
			Debug.Assert( newAnchor <= Length );
			Debug.Assert( newCaret <= Length );

			// cast event
			if( _IsSuppressingDirtyStateChangedEvent == false
				&& _History.IsSavedState != wasSavedState )
			{
				InvokeDirtyStateChanged();
			}
			InvokeContentChanged( begin, oldText, text );
			InvokeSelectionChanged( oldAnchor, oldCaret, null, true );
		}
		#endregion

		#region Marking
		/// <summary>
		/// Marks up specified text range.
		/// </summary>
		/// <param name="begin">The index of where the range begins.</param>
		/// <param name="end">The index of where the range ends.</param>
		/// <param name="markingID">ID of marking to be set.</param>
		/// <returns>Whether the operation changed previous marking data or not.</returns>
		/// <exception cref="System.ArgumentOutOfRangeException">
		///   Parameter <paramref name="begin"/> or <paramref name="end"/> is out of valid range.
		///   - OR - Parameter <paramref name="markingID"/> is out of valid range.
		///	</exception>
		/// <exception cref="System.ArgumentException">
		///   Parameter <paramref name="begin"/> is equal or greater than <paramref name="end"/>.
		///   - OR - Parameter <paramref name="markingID"/> is not registered to Marking class.
		///	</exception>
		/// <remarks>
		///   <para>
		///   This method marks up a range of text with ID of 'marking'.
		///   </para>
		///	  <para>
		///	  For detail of marking feature, please refer to the document of
		///	  <see cref="Marking"/> class.
		///	  </para>
		/// </remarks>
		/// <seealso cref="Document.Unmark"/>
		/// <seealso cref="Marking"/>
		public bool Mark( int begin, int end, int markingID )
		{
			if( begin < 0 || _Buffer.Count < begin )
				throw new ArgumentOutOfRangeException( "begin", "Invalid index was given. (begin:"
													   + begin + "," + " Length:" + Length + ")" );
			if( end < 0 || _Buffer.Count < end )
				throw new ArgumentOutOfRangeException( "end", "Invalid index was given. (end:"
													   + end + "," + " Length:" + Length + ")" );
			if( end < begin )
				throw new ArgumentException( "Parameter 'begin' must not be greater than 'end'."
											 + " (begin:" + begin + ", end:" + end + ")" );
			if( Marking.GetMarkingInfo(markingID) == null )
				throw new ArgumentException( "Specified marking ID is not registered. (markingID:"
											 + markingID + ")", "markingID" );

			Debug.Assert( _Buffer.Marks.Count == Length, "sync failed." );

			var changed = false;

			// store the marking ID in form of bit mask
			var bitMask = (uint)( 0x01 << markingID );
			for( int i=begin; i<end; i++ )
			{
				if( (_Buffer.Marks[i] & bitMask) == 0 )
				{
					_Buffer.Marks[i] |= (uint)bitMask;
					changed = true;
				}
			}

			return changed;
		}

		/// <summary>
		/// Removes specified type of marking information at specified range.
		/// </summary>
		/// <param name="begin">The index of where the range begins.</param>
		/// <param name="end">The index of where the range ends.</param>
		/// <param name="markingID">The ID of the marking to be removed.</param>
		/// <returns>Whether any marking data was removed or not.</returns>
		/// <exception cref="System.ArgumentOutOfRangeException">
		///   Parameter <paramref name="begin"/> or <paramref name="end"/> is out of valid range.
		///   - OR - Parameter <paramref name="markingID"/> is out of valid range.
		///	</exception>
		/// <exception cref="System.ArgumentException">
		///   Parameter <paramref name="begin"/> is equal or greater than <paramref name="end"/>.
		///   - OR - Parameter <paramref name="markingID"/> is not registered to Marking class.
		///	</exception>
		///	<remarks>
		///	  <para>
		///	  This method scans range of [<paramref name="begin"/>, <paramref name="end"/>)
		///	  and removes specified marking ID.
		///	  </para>
		///	  <para>
		///	  For detail of marking feature, please refer to the document of
		///	  <see cref="Marking"/> class.
		///	  </para>
		///	</remarks>
		/// <seealso cref="Marking"/>
		/// <seealso cref="Document.Mark"/>
		public bool Unmark( int begin, int end, int markingID )
		{
			if( begin < 0 || _Buffer.Count < begin )
				throw new ArgumentOutOfRangeException( "begin", "Invalid index was given."
													   + " (begin:" + begin
													   + ", this.Length:" + Length + ")" );
			if( end < 0 || _Buffer.Count < end )
				throw new ArgumentOutOfRangeException( "end", "Invalid index was given."
													   + " (end:" + end
													   + ", this.Length:" + Length + ")" );
			if( end < begin )
				throw new ArgumentException( "Parameter 'begin' must not be greater than 'end'."
											 + " (begin:" + begin + ", end:" + end + ")" );
			if( Marking.GetMarkingInfo(markingID) == null )
				throw new ArgumentException( "Specified marking ID is not registered."
											 + " (markingID:" + markingID + ")", "markingID" );

			Debug.Assert( _Buffer.Marks.Count == Length, "sync failed." );

			var changed = false;

			// clears bit of the marking
			var bitMask = (uint)( 0x01 << markingID );
			for( int i=begin; i<end; i++ )
			{
				if( (_Buffer.Marks[i] & bitMask) != 0 )
				{
					_Buffer.Marks[i] &= (uint)( ~bitMask );
					changed = true;
				}
			}

			return changed;
		}

		/// <summary>
		/// Gets range of text part which includes specified index
		/// which is marked with specified ID.
		/// </summary>
		/// <param name="index">
		///   The text range including a character at this index will be retrieved.
		/// </param>
		/// <param name="markingID">
		///   The text range marked with this ID will be retrieved.
		/// </param>
		/// <returns>
		///   Range of the text part if specified index was marked with the ID.
		///   Otherwise returns an empty range.
		/// </returns>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="index"/> is out of valid range.
		///   - OR - Parameter <paramref name="markingID"/> is out of valid range.
		/// </exception>
		/// <exception cref="ArgumentException">
		///   Parameter <paramref name="markingID"/> is not registered in Marking class.
		/// </exception>
		/// <seealso cref="Marking"/>
		public IRange GetMarkedRange( int index, int markingID )
		{
			if( index < 0 || _Buffer.Count <= index )
				throw new ArgumentOutOfRangeException( "index",
													   "Specified index is out of valid range."
													   + " (index:" + index
													   + ", Document.Length:" + Length + ")" );
			if( Marking.GetMarkingInfo(markingID) == null )
				throw new ArgumentException( "Specified marking ID is not registered."
											 + " (markingID:" + markingID + ")",
											 "markingID" );

			var range = new Range( index, index );

			// make bit mask
			var markingBitMask = (uint)( 1 << markingID );
			if( (_Buffer.Marks[index] & markingBitMask) == 0 )
			{
				return range;
			}

			// seek back until the marking bit was disabled
			range.Begin = index;
			while( 0 <= range.Begin-1
				&& (_Buffer.Marks[range.Begin-1] & markingBitMask) != 0 )
			{
				range.Begin--;
			}

			// seek forward until the marking bit was disabled
			range.End = index;
			while( range.End < _Buffer.Count
				&& (_Buffer.Marks[range.End] & markingBitMask) != 0 )
			{
				range.End++;
			}

			return new Range( range.Begin, range.End );
		}

		/// <summary>
		/// Gets text part marked with specified ID at specified index.
		/// </summary>
		/// <param name="index">The marked text part at this index will be retrieved.</param>
		/// <param name="markingID">The text part marked with this ID will be retrieved.</param>
		/// <returns>The text if found, otherwise null.</returns>
		/// <exception cref="System.ArgumentOutOfRangeException">
		///   Parameter <paramref name="index"/> is out of valid range.
		///   - OR - Parameter <paramref name="markingID"/> is out of valid range.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		///   Parameter <paramref name="markingID"/> is not registered in Marking class.
		/// </exception>
		public string GetMarkedText( int index, int markingID )
		{
			if( index < 0 || _Buffer.Count <= index )
				throw new ArgumentOutOfRangeException( "index",
													   "Specified index is out of valid range."
													   + " (index:" + index + ", Document.Length:"
													   + Length + ")" );
			if( Marking.GetMarkingInfo(markingID) == null )
				throw new ArgumentException( "Specified marking ID is not registered."
											 + " (markingID:" + markingID + ")",
											 "markingID" );

			// get range of the marked text
			var range = GetMarkedRange( index, markingID );
			if( range.IsEmpty )
			{
				return null;
			}

			// extract that range
			return GetText( range.Begin, range.End );
		}

		/// <summary>
		/// Determine whether specified index is marked with specified marking ID or not.
		/// </summary>
		/// <param name="index">The index to examine.</param>
		/// <param name="markingID">
		///   Whether specified index is marked with this ID will be retrieved.
		///	</param>
		/// <returns>
		///   Whether a character at <paramref name="index"/> is
		///   marked with <paramref name="markingID"/> or not.
		/// </returns>
		/// <exception cref="System.ArgumentOutOfRangeException">
		///   Parameter <paramref name="index"/> is out of valid range.
		///   - OR - Parameter <paramref name="markingID"/> is out of valid range.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		///   Parameter <paramref name="markingID"/> is not registered in Marking class.
		/// </exception>
		public bool IsMarked( int index, int markingID )
		{
			if( index < 0 || _Buffer.Count <= index )
				throw new ArgumentOutOfRangeException( "index",
													   "Specified index is out of valid range."
													   + " (index:" + index
													   + ", Document.Length:" + Length + ")" );
			if( Marking.GetMarkingInfo(markingID) == null )
				throw new ArgumentException( "Specified marking ID is not registered."
											 + " (markingID:" + markingID + ")",
											 "markingID" );

			var markingBitMask = GetMarkingBitMaskAt( index );
			return ( (markingBitMask >> markingID) & 0x01) != 0;
		}

		/// <summary>
		/// List up all markings at specified index and returns their IDs as an array.
		/// </summary>
		/// <param name="index">The index of the position to examine.</param>
		/// <returns>Array of marking IDs if any marking found, or an empty array if no marking found.</returns>
		/// <remarks>
		///   <para>
		///   This method does not throw exception
		///   but returns an empty array if end index of the document
		///   (index equal to length of document) was specified.
		///   </para>
		/// </remarks>
		/// <seealso cref="Marking">Marking class</seealso>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="index"/> is out of valid range.
		/// </exception>
		public int[] GetMarkingsAt( int index )
		{
			if( index < 0 || _Buffer.Count < index )
				throw new ArgumentOutOfRangeException( "index", "Specified index is out of valid range. (index:"+index+", Document.Length:"+Length+")" );

			var result = new List<int>( 8 );

			// if specified index is end of document, no marking will be found anyway
			if( _Buffer.Count == index )
			{
				return result.ToArray();
			}

			// get marking bit mask of specified character
			var markingBitMask = _Buffer.Marks[ index ];
			if( markingBitMask == 0 )
			{
				return result.ToArray();
			}

			// create an array of marking IDs
			for( int i=0; i<=Marking.MaxID; i++ )
			{
				if( (markingBitMask & 0x01) != 0 )
				{
					result.Add( i );
				}
				markingBitMask >>= 1;
			}

			return result.ToArray();
		}

		/// <summary>
		/// Gets marking IDs at specified index as a bit mask (internal representation).
		/// </summary>
		/// <param name="index">The marking IDs put on the character at this index will be returned.</param>
		/// <returns>Bit mask represents markings which covers the character.</returns>
		/// <remarks>
		///   <para>
		///   This method gets a bit-masked integer representing
		///   which marking IDs are put on that position.
		///   </para>
		///	  <para>
		///	  For detail of marking feature, please refer to the document of
		///	  <see cref="Marking"/> class.
		///	  </para>
		/// </remarks>
		/// <seealso cref="Marking"/>
		/// <seealso cref="Document.GetMarkingsAt"/>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="index"/> is out of valid range.
		/// </exception>
		public uint GetMarkingBitMaskAt( int index )
		{
			if( index < 0 || _Buffer.Count <= index )
				throw new ArgumentOutOfRangeException( "index", "Specified index is out of valid range. (index:"+index+", Document.Length:"+Length+")" );

			return _Buffer.Marks[index];
		}

		/// <summary>
		/// Gets or sets whether URIs in this document
		/// should be marked automatically with built-in URI marker or not.
		/// </summary>
		/// <remarks>
		///   <para>
		///   Note that built-in URI marker marks URIs in document
		///   and then Azuki shows the URIs as 'looks like URI,'
		///   but (1) clicking mouse button on them, or
		///   (2) pressing keys when the caret is at middle of a URI,
		///   makes NO ACTION BY DEFAULT.
		///   To define action on such event,
		///   programmer must implement such action as a part of
		///   event handler of standard mouse event or keyboard event.
		///   Please refer to the
		///   <see cref="Marking">document of marking feature</see> for details.
		///   </para>
		/// </remarks>
		public bool MarksUri
		{
			get; set;
		}

		internal WatchPatternMarker WatchPatternMarker
		{
			get{ return _WatchPatternMarker; }
		}
		#endregion

		#region Editing Behavior
		/// <summary>
		/// Begins grouping up editing actions into a single undo action.
		/// </summary>
		/// <remarks>
		///   <para>
		///   Creates an undo context and starts to collect modifications to this document into a
		///   single undoable action until the context was closed.
		///   </para>
		///   <para>
		///   If no actions were collected in an undo context, closing the context creates an empty
		///   undo action. Undoing an empty undo action do nothing.
		///   </para>
		///   <para>
		///   Undo contexts can be stacked. If you call this method multiple times and get multiple
		///   context objects, the undo context will be closed when every context object was
		///   disposed. Internally, the number of undo contexts are counted.
		///   </para>
		/// </remarks>
		/// <seealso cref="Document.Undo"/>
		/// <seealso cref="Document.CanUndo"/>
		public IDisposable BeginUndo()
		{
			_History.BeginUndo();
			return new UndoContext( this );
		}

		internal void EndUndo()
		{
			_History.EndUndo();
		}

		/// <summary>
		/// Executes UNDO.
		/// </summary>
		/// <remarks>
		///   <para>
		///   This method reverses the effect of a modification lastly done to this document. If
		///   there was no executable undo action, this method does nothing.
		///   </para>
		///   <para>
		///   To get whether any undoable action exists or not, use <see cref="Document.CanUndo"/>
		///   property.
		///   </para>
		/// </remarks>
		/// <seealso cref="Document.BeginUndo"/>
		/// <seealso cref="Document.CanUndo"/>
		public void Undo()
		{
			// First of all, stop grouping actions
			if( _History.IsGroupingActions )
				EndUndo();

			if( CanUndo == false )
				return;

			var wasSavedState = _History.IsSavedState;

			// Get the action to be undone.
			var action = _History.GetUndoAction();
			Debug.Assert( action != null );

			// Undo the action
			_IsSuppressingDirtyStateChangedEvent = true;	// To prevent fireing DirtyStateChanged
			{												// event multiple times
				action.Undo();
			}
			_IsSuppressingDirtyStateChangedEvent = false;

			// Invoke event if this operation changes dirty state of this document
			if( _History.IsSavedState != wasSavedState )
			{
				InvokeDirtyStateChanged();
			}
		}

		/// <summary>
		/// Clears all stacked edit histories.
		/// </summary>
		/// <remarks>
		///   <para>
		///   This method clears all editing histories for
		///   UNDO or REDO action in this document.
		///   </para>
		///   <para>
		///   Note that calling this method will not invalidate graphics.
		///   To update graphic, use IUserInterface.ClearHistory or update manually.
		///   </para>
		/// </remarks>
		/// <seealso cref="IUserInterface.ClearHistory"/>
		public void ClearHistory()
		{
			_History.Clear();
			_History.SetSavedState();
			for( int i=0; i<Lines.Count; i++ )
				_Buffer.GetLineRange(i).DirtyState = DirtyState.Clean;
		}

		/// <summary>
		/// Executes REDO.
		/// </summary>
		/// <remarks>
		///   <para>
		///   This method executes the undo action which was lastly executed. If there is no
		///   redo-able action, this method will do nothing.
		///   </para>
		/// </remarks>
		public void Redo()
		{
			// first of all, stop grouping actions
			// (this must be done before evaluating CanRedo because EndUndo changes it)
			if( _History.IsGroupingActions )
				EndUndo();

			if( CanRedo == false )
				return;

			bool wasSavedState = _History.IsSavedState;

			// Get the action to be done again.
			EditAction action = _History.GetRedoAction();
			Debug.Assert( action != null );

			// Redo the action.
			// Note that an REDO may includes multiple actions
			// so executing it may call Document.Replace multiple times.
			// Because Document.Replace also invokes DirtyStateChanged event by itself,
			// designing to make sure Document.Replace called in REDO is rather complex.
			// So here I use a special flag to supress invoking event in Document.Replace
			// ... to make sure unnecessary events will never be invoked.
			_IsSuppressingDirtyStateChangedEvent = true;
			{
				action.Redo();
			}
			_IsSuppressingDirtyStateChangedEvent = false;

			// Invoke event if this operation
			// changes dirty state of this document
			if( _History.IsSavedState != wasSavedState )
			{
				InvokeDirtyStateChanged();
			}
		}

		/// <summary>
		/// Gets or sets default EOL Code of this document.
		/// </summary>
		/// <exception cref="InvalidOperationException">Specified EOL code is not supported.</exception>
		/// <remarks>
		///   <para>
		///   This value will be used when an Enter key was pressed,
		///   but setting this property itself does nothing to the content.
		///   </para>
		/// </remarks>
		public string EolCode
		{
			get{ return _EolCode; }
			set
			{
				if( value != "\r\n" && value != "\r" && value != "\n" )
					throw new InvalidOperationException( "unsupported type of EOL code was set." );
				_EolCode = value;
			}
		}

		/// <summary>
		/// Gets or sets how to select text.
		/// </summary>
		public TextDataType SelectionMode
		{
			get{ return _SelMan.SelectionMode; }
			set{ _SelMan.SelectionMode = value; }
		}
		#endregion

		#region Index Conversion
		/// <summary>
		/// Calculates line/column index from an offset.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException"/>
		public LineColumnPos GetLineColumnPos( int charIndex )
		{
			return _Buffer.GetLineColumnPos( charIndex );
		}

		/// <summary>
		/// Calculates char-index from line/column index.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException"/>
		public int GetCharIndex( LineColumnPos pos )
		{
			if( pos.Line < 0 || Lines.Count <= pos.Line || pos.Column < 0 )
				throw new ArgumentOutOfRangeException( "pos", "Invalid index was given (pos:" + pos
													   + ", Lines.Count:" + Lines.Count + ")." );

			return _Buffer.GetCharIndex( pos );
		}
		#endregion

		#region Text Search
		/// <summary>
		/// Finds a text pattern.
		/// </summary>
		/// <param name="value">The string to find.</param>
		/// <param name="startIndex">The index of the position to start searching.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="value"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="startIndex"/> is negative or greater than length of
		///   this document.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for the first occurrence of the pattern in
		///   [<paramref name="startIndex"/>, EOD) case-sensitively where EOD means the end of
		///   document. If parameter <paramref name="value"/> is an empty string, search result
		///   will be [<paramref name="startIndex"/>, <paramref name="startIndex"/>).
		///   </para>
		///   <para>
		///   If you want to limit length to search, use <see cref="FindNext(string, int, int)"/>.
		///   </para>
		///   <para>
		///   To search case-insensitively, use <see cref="FindNext(string, int, bool)"/>.
		///   </para>
		/// </remarks>
		public IRange FindNext( string value, int startIndex )
		{
			return FindNext( value, startIndex, _Buffer.Count, true );
		}

		/// <summary>
		/// Finds a text pattern.
		/// </summary>
		/// <param name="value">The string to find.</param>
		/// <param name="begin">The index of the position to start searching.</param>
		/// <param name="end">The index of the position to stop searching.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="value"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="begin"/> or <paramref name="end"/> is out of valid range.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for the first occurrence of the pattern case-sensitively in
		///   [<paramref name="begin"/>, <paramref name="end"/>). If parameter
		///   <paramref name="value"/> is an empty string, search result will be
		///   [<paramref name="begin"/>, <paramref name="begin"/>).
		///   </para>
		///   <para>
		///   To search case-insensitively, use <see cref="FindNext(string, int, int, bool)"/>.
		///   </para>
		/// </remarks>
		public IRange FindNext( string value, int begin, int end )
		{
			return FindNext( value, begin, end, true );
		}

		/// <summary>
		/// Finds a text pattern.
		/// </summary>
		/// <param name="value">The string to find.</param>
		/// <param name="startIndex">The index of the position to start searching.</param>
		/// <param name="matchCase">Whether the search should be case-sensitive or not.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="value"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="startIndex"/> is negative or greater than length of this
		///   document.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for the first occurrence of the pattern in
		///   [<paramref name="startIndex"/>, EOD) where EOD means the end of document. If
		///   parameter <paramref name="value"/> is an empty string, search result will be
		///   [<paramref name="startIndex"/>, <paramref name="startIndex"/>).
		///   </para>
		///   <para>
		///   If you want to limit length to search, use
		///   <see cref="FindNext(string, int, int, bool)"/>.
		///   </para>
		/// </remarks>
		public IRange FindNext( string value, int startIndex, bool matchCase )
		{
			return FindNext( value, startIndex, _Buffer.Count, matchCase );
		}

		/// <summary>
		/// Finds a text pattern.
		/// </summary>
		/// <param name="value">The string to find.</param>
		/// <param name="begin">The index of the position to start searching.</param>
		/// <param name="end">The index of the position to stop searching.</param>
		/// <param name="matchCase">Whether the search should be case-sensitive or not.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="value"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="begin"/> or <paramref name="end"/> is out of valid range.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for the first occurrence of the pattern in 
		///   [<paramref name="begin"/>, <paramref name="end"/>). If parameter
		///   <paramref name="value"/> is an empty string, search result will be
		///   [<paramref name="begin"/>, <paramref name="begin"/>).
		///   </para>
		/// </remarks>
		public IRange FindNext( string value, int begin, int end, bool matchCase )
		{
			return _Buffer.FindNext( value, begin, end, matchCase );
		}

		/// <summary>
		/// Finds a text pattern by regular expression.
		/// </summary>
		/// <param name="regex">Regular expression of the text pattern to find.</param>
		/// <param name="startIndex">The index of the position to start searching.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentException">
		///   Parameter <paramref name="regex"/> is a Regex object initialized with
		///   RegexOptions.RightToLeft option.
		/// </exception>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="regex"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="startIndex"/> is negative or greater than length of this
		///   document.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for a text pattern which matches specified regular expression in
		///   [<paramref name="startIndex"/>, EOD) where EOD means the end of document. If an empty
		///   string was used for a regular expression pattern, search result will be
		///   [<paramref name="startIndex"/>, <paramref name="startIndex"/>).
		///   </para>
		///   <para>
		///   If you want to limit length to search, use <see cref="FindNext(Regex, int, int)"/>.
		///   </para>
		/// </remarks>
		public IRange FindNext( Regex regex, int startIndex )
		{
			return FindNext( regex, startIndex, _Buffer.Count );
		}

		/// <summary>
		/// Finds a text pattern by regular expression.
		/// </summary>
		/// <param name="regex">Regular expression of the text pattern to find.</param>
		/// <param name="begin">The index of the position to start searching.</param>
		/// <param name="end">The index of the position to stop searching.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentException">
		///   Parameter <paramref name="regex"/> is a Regex object initialized with
		///   RegexOptions.RightToLeft option.
		/// </exception>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="regex"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="begin"/> or <paramref name="end"/> is out of valid range.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for a text pattern which matches specified regular expression in
		///   [<paramref name="begin"/>, <paramref name="end"/>). If an empty string was used for a
		///   regular expression pattern, search result will be
		///   [<paramref name="begin"/>, <paramref name="begin"/>).
		///   </para>
		/// </remarks>
		public IRange FindNext( Regex regex, int begin, int end )
		{
			return _Buffer.FindNext( regex, begin, end );
		}

		/// <summary>
		/// Finds a text pattern backward.
		/// </summary>
		/// <param name="value">The string to find.</param>
		/// <param name="startIndex">The index of the position to start searching.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="value"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="startIndex"/> is out of valid range.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for the last occurrence of the pattern case-sensitively in
		///   [0, <paramref name="startIndex"/>). If parameter <paramref name="value"/> is an empty
		///   string, search result will be
		///   [<paramref name="startIndex"/>, <paramref name="startIndex"/>).
		///   </para>
		///   <para>
		///   If you want to limit length to search, use <see cref="FindPrev(string, int, int)"/>.
		///   </para>
		///   <para>
		///   To search case-insensitively, use <see cref="FindPrev(string, int, bool)"/>.
		///   </para>
		/// </remarks>
		public IRange FindPrev( string value, int startIndex )
		{
			return FindPrev( value, 0, startIndex, true );
		}

		/// <summary>
		/// Finds a text pattern backward.
		/// </summary>
		/// <param name="value">The string to find.</param>
		/// <param name="startIndex">The index of the position to start searching.</param>
		/// <param name="matchCase">Whether the search should be case-sensitive or not.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="value"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="startIndex"/> is negative or greater than length of this
		///   document.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for the last occurrence of the pattern in
		///   [0, <paramref name="startIndex"/>). If parameter <paramref name="value"/> is an empty
		///   string, search result will be
		///   [<paramref name="startIndex"/>, <paramref name="startIndex"/>).
		///   </para>
		///   <para>
		///   If you want to limit length to search, use
		///   <see cref="FindPrev(string, int, int, bool)"/>.
		///   </para>
		/// </remarks>
		public IRange FindPrev( string value, int startIndex, bool matchCase )
		{
			return FindPrev( value, 0, startIndex, matchCase );
		}

		/// <summary>
		/// Finds a text pattern backward.
		/// </summary>
		/// <param name="value">The string to find.</param>
		/// <param name="begin">The index of the position to start searching.</param>
		/// <param name="end">The index of the position to stop searching.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="value"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="begin"/> or <paramref name="end"/> is out of valid range.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for the last occurrence of the pattern case-sensitively in
		///   [<paramref name="begin"/>, <paramref name="end"/>). If parameter
		///   <paramref name="value"/> is an empty string, search result will be
		///   [<paramref name="end"/>, <paramref name="end"/>).
		///   </para>
		///   <para>
		///   To search case-insensitively, use <see cref="FindPrev(string, int, int, bool)"/>.
		///   </para>
		/// </remarks>
		public IRange FindPrev( string value, int begin, int end )
		{
			return FindPrev( value, begin, end, true );
		}

		/// <summary>
		/// Finds a text pattern backward.
		/// </summary>
		/// <param name="value">The string to find.</param>
		/// <param name="begin">The index of the position to start searching.</param>
		/// <param name="end">The index of the position to stop searching.</param>
		/// <param name="matchCase">Whether the search should be case-sensitive or not.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="value"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="begin"/> or <paramref name="end"/> is out of valid range.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for the last occurrence of the pattern case-sensitively in
		///   [<paramref name="begin"/>, <paramref name="end"/>). If parameter
		///   <paramref name="value"/> is an empty string, search result will be
		///   [<paramref name="end"/>, <paramref name="end"/>).
		///   </para>
		/// </remarks>
		public IRange FindPrev( string value, int begin, int end, bool matchCase )
		{
			return _Buffer.FindPrev( value, begin, end, matchCase );
		}

		/// <summary>
		/// Finds a text pattern backward by regular expression.
		/// </summary>
		/// <param name="regex">Regular expression of the text pattern to find.</param>
		/// <param name="startIndex">The index of the position to start searching.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentException">
		///   Parameter <paramref name="regex"/> is not a Regex object initialized with
		///   RegexOptions.RightToLeft option.
		/// </exception>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="regex"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="startIndex"/> is negative or greater than length of this
		///   document.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for last occurence of a text pattern which matches specified
		///   regular expression in [0, <paramref name="startIndex"/>). If an empty string was used
		///   as a regular expression, search result will be
		///   [<paramref name="startIndex"/>, <paramref name="startIndex"/>).
		///   </para>
		///   <para>
		///   If you want to limit length to search, use
		///   <see cref="FindPrev(Regex, int, int)"/>.
		///   </para>
		/// </remarks>
		public IRange FindPrev( Regex regex, int startIndex )
		{
			return FindPrev( regex, 0, startIndex );
		}

		/// <summary>
		/// Finds a text pattern backward by regular expression.
		/// </summary>
		/// <param name="regex">Regular expression of the text pattern to find.</param>
		/// <param name="begin">The index of the position to start searching.</param>
		/// <param name="end">The index of the position to stop searching.</param>
		/// <returns>Range of the firstly found pattern or null if not found.</returns>
		/// <exception cref="ArgumentException">
		///   Parameter <paramref name="regex"/> is not a Regex object initialized with
		///   RegexOptions.RightToLeft option.
		/// </exception>
		/// <exception cref="ArgumentNullException">
		///   Parameter <paramref name="regex"/> is null.
		/// </exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///   Parameter <paramref name="begin"/> or <paramref name="end"/> is out of valid range.
		/// </exception>
		/// <remarks>
		///   <para>
		///   This method searches for last occurence of a text pattern which matches specified
		///   regular expression in [<paramref name="begin"/>, <paramref name="end"/>). If an empty
		///   string was used as a regular expression, search result will be
		///   [<paramref name="end"/>, <paramref name="end"/>).
		///   </para>
		/// </remarks>
		public IRange FindPrev( Regex regex, int begin, int end )
		{
			return _Buffer.FindPrev( regex, begin, end );
		}

		/// <summary>
		/// Finds matched bracket from specified index.
		/// </summary>
		/// <param name="index">The index to start searching matched bracket.</param>
		/// <returns>Index of the matched bracket if found. Otherwise -1.</returns>
		/// <remarks>
		///   <para>
		///   This method searches the matched bracket from specified index.
		///   If the character at specified index was not a sort of bracket,
		///   or if specified index points to a character
		///   which has no meaning on grammar (such as comment block, string literal, etc.),
		///   this method returns -1.
		///   </para>
		/// </remarks>
		public int FindMatchedBracket( int index )
		{
			return FindMatchedBracket( index, -1 );
		}

		/// <summary>
		/// Finds matched bracket from specified index.
		/// </summary>
		/// <param name="index">The index to start searching matched bracket.</param>
		/// <param name="maxSearchLength">Maximum number of characters to search matched bracket for.</param>
		/// <returns>Index of the matched bracket if found. Otherwise -1.</returns>
		/// <remarks>
		///   <para>
		///   This method searches the matched bracket from specified index.
		///   If the character at specified index was not a sort of bracket,
		///   or if specified index points to a character
		///   which has no meaning on grammar (such as comment block, string literal, etc.),
		///   this method returns -1.
		///   </para>
		/// </remarks>
		public int FindMatchedBracket( int index, int maxSearchLength )
		{
			if( index < 0 || Length < index )
				throw new ArgumentOutOfRangeException( "index" );

			char bracket, pairBracket;
			bool isOpenBracket = false;
			int depth;

			// Quit searching; there is no matched bracket if given index is the end position
			if( Length == index || IsCDATA(index) )
			{
				return -1;
			}

			// Get the bracket and its pair
			bracket = this[index];
			pairBracket = '\0';
			for( int i=0; i<_PairBracketTable.Length; i++ )
			{
				if( bracket == _PairBracketTable[i] )
				{
					if( (i & 0x01) == 0 )
					{
						// Found bracket is an opener. get paired closer
						pairBracket = _PairBracketTable[i+1];
						isOpenBracket = true;
					}
					else
					{
						// Found bracket is a closer. get paired opener
						pairBracket = _PairBracketTable[i-1];
						isOpenBracket = false;
					}
					break;
				}
			}
			if( pairBracket == '\0' )
			{
				return -1; // not a bracket.
			}

			// search matched one
			depth = 0;
			if( isOpenBracket )
			{
				// determine search ending position
				int limit = Length;
				if( 0 < maxSearchLength )
					limit = Math.Min( Length, index+maxSearchLength );

				// search
				for( int i=index; i<limit; i++ )
				{
					// if it is in comment or something that is not a part of "content," ignore it
					if( IsCDATA(i) )
						continue;

					if( this[i] == bracket )
					{
						// found an opener again. increment depth count
						depth++;
					}
					else if( this[i] == pairBracket )
					{
						// found an closer. decrement depth count
						depth--;
						if( depth == 0 )
						{
							return i; // depth count reset by this char; this is the pair
						}
					}
				}
			}
			else
			{
				// determine search ending position
				int limit = 0;
				if( 0 < maxSearchLength )
					limit = Math.Max( 0, index-maxSearchLength );

				// search
				for( int i=index; limit<=i; i-- )
				{
					// if it is in comment or something that is not a part of "content," ignore it
					if( IsCDATA(i) )
						continue;

					if( this[i] == bracket )
					{
						// found an closer again. increment depth count
						depth++;
					}
					else if( this[i] == pairBracket )
					{
						// found an opener. decrement depth count
						depth--;
						if( depth == 0 )
						{
							return i; // depth count reset by this char; this is the pair
						}
					}
				}
			}

			// not found
			return -1;
		}
		#endregion

		#region Highlighter and word processor
		/// <summary>
		/// Gets or sets syntax highlighter for this document (setting null disables highlighting.)
		/// </summary>
		/// <remarks>
		///   <para>
		///   This property gets or sets a syntax highlighter object for this document. If null was
		///   given, highlighting feature will be disabled.
		///   </para>
		///   <para>
		///   Syntax highlighters are objects implementing <see cref="IHighlighter"/>. There are
		///   some built-in syntax highlighters provided as members of <see cref="Highlighters"/>
		///   class. If there isn't an appropriate built-in highlighter, you can create your own
		///   syntax highlighter by implementing <see cref="IHighlighter"/>. Note that if it is a
		///   keyword-based syntax highlighter, it is recommended to create a class which extends
		///   <see cref="KeywordHighlighter"/> because it provides highlighting logic which
		///   satisfies most needs.
		///   </para>
		///   <para>
		///   Azuki calls <see cref="IHighlighter.Highlight"/> every time slightly after the user
		///   stops editing with a range of document which should be highlighted. This means
		///   highlighting does not occur in realtime.
		///   </para>
		/// </remarks>
		public IHighlighter Highlighter
		{
			get{ return _Highlighter; }
			set
			{
				_Buffer.ClearCharClasses();
				_Highlighter = value;
				InvokeHighlighterChanged();
			}
		}

		/// <summary>
		/// Gets whether the character at specified index
		/// is just a data without meaning on grammar.
		/// </summary>
		/// <param name="index">The index of the character to examine.</param>
		/// <returns>Whether the character is part of a character data or not.</returns>
		/// <remarks>
		///   <para>
		///   This method gets whether the character at specified index
		///   is just a character data without meaning on grammar.
		///   'Character data' here means text data which is not a part of the grammar.
		///   Example of character data is comment or string literal in programming languages.
		///   </para>
		/// </remarks>
		public bool IsCDATA( int index )
		{
			var klass = GetCharClass( index );
			return ( klass == CharClass.AttributeValue
					|| klass == CharClass.CDataSection
					|| klass == CharClass.Character
					|| klass == CharClass.Comment
					|| klass == CharClass.DocComment
					|| klass == CharClass.Regex
					|| klass == CharClass.String
				);
		}

		/// <summary>
		/// Gets or sets word processor object which determines how Azuki handles 'words.'
		/// </summary>
		/// <remarks>
		///   <para>
		///   This property gets or sets word processor object.
		///   Please refer to the document of IWordProc interface for detail.
		///   </para>
		/// </remarks>
		/// <seealso cref="IWordProc"/>
		/// <seealso cref="DefaultWordProc"/>
		public IWordProc WordProc
		{
			get{ return _WordProc; }
			set
			{
				if( value == null )
					value = new DefaultWordProc();
				_WordProc = value;
			}
		}
		#endregion

		#region Events
		/// <summary>
		/// Occurs when the selection was changed.
		/// </summary>
		public event SelectionChangedEventHandler SelectionChanged;
		internal void InvokeSelectionChanged( int oldAnchor,
											  int oldCaret,
											  int[] oldRectSelectRanges,
											  bool byContentChanged )
		{
#			if DEBUG
			Debug.Assert( 0 <= oldAnchor );
			Debug.Assert( 0 <= oldCaret );
			if( oldRectSelectRanges != null )
			{
				Debug.Assert( oldRectSelectRanges.Length % 2 == 0 );
			}
#			endif

			if( SelectionChanged != null )
			{
				SelectionChanged( this,
								  new SelectionChangedEventArgs(oldAnchor,
																oldCaret,
																oldRectSelectRanges,
																byContentChanged) );
			}
		}

		/// <summary>
		/// Occurs when the document content was changed.
		/// </summary>
		/// <remarks>
		/// <see cref="ContentChangedEventArgs"/> contains the old (replaced) text,
		/// new text, and index indicating the replacement occured.
		/// </remarks>
		public event ContentChangedEventHandler ContentChanged;
		void InvokeContentChanged( int index, string oldText, string newText )
		{
			Debug.Assert( 0 <= index );
			Debug.Assert( oldText != null );
			Debug.Assert( newText != null );

			if( ContentChanged != null )
				ContentChanged( this, new ContentChangedEventArgs(index, oldText, newText) );
		}

		/// <summary>
		/// Occurs when the IsDirty property was changed.
		/// </summary>
		/// <seealso cref="Document.IsDirty"/>
		public event EventHandler DirtyStateChanged;
		void InvokeDirtyStateChanged()
		{
			if( DirtyStateChanged != null )
				DirtyStateChanged( this, EventArgs.Empty );
		}

		/// <summary>
		/// Occurs when the selection mode was changed.
		/// </summary>
		/// <seealso cref="Document.SelectionMode"/>
		public event EventHandler SelectionModeChanged;
		internal void InvokeSelectionModeChanged()
		{
			if( SelectionModeChanged != null )
			{
				SelectionModeChanged( this, EventArgs.Empty );
			}
		}

		/// <summary>
		/// Occurs when the highlighter was changed.
		/// </summary>
		/// <seealso cref="Document.Highlighter"/>
		public event EventHandler HighlighterChanged;
		void InvokeHighlighterChanged()
		{
			if( HighlighterChanged != null )
				HighlighterChanged( this, EventArgs.Empty );
		}
		#endregion

		#region Utilities
		/// <summary>
		/// Gets or sets an object associated with this document.
		/// </summary>
		public object Tag
		{
			get; set;
		}

		/// <summary>
		/// Gets a range object which covers the entire range of this document.
		/// </summary>
		public Range ToRange()
		{
			return new Range( this, 0, Length );
		}

		/// <summary>
		/// Gets a range object which covers the entire range of this document.
		/// </summary>
		public static explicit operator Range( Document doc )
		{
			return doc.ToRange();
		}

		/// <summary>
		/// Gets index of next grapheme cluster.
		/// </summary>
		/// <param name="index">The index to start the search from.</param>
		/// <returns>The index of the character which starts next grapheme cluster.</returns>
		/// <exception cref="System.ArgumentOutOfRangeException">Parameter '<paramref name="index"/>' is out of valid range.</exception>
		/// <remarks>
		///   <para>
		///   This method searches document for a grapheme cluster
		///   from given <paramref name="index"/> forward.
		///   Note that this method always return an index greater than given '<paramref name="index"/>'.
		///   </para>
		///   <para>
		///   'Grapheme cluster' is a sequence of characters
		///   which consists one 'user perceived character'
		///   such as sequence of U+0041 and U+0300; a capital 'A' with grave (&#x0041;&#x0300;).
		///   In most cases, such sequence should not be divided unless user wishes to do so.
		///   </para>
		///   <para>
		///   This method determines an index pointing the middle of character sequences next as undividable:
		///   </para>
		///   <list type="bullet">
		///     <item>CR+LF</item>
		///     <item>Surrogate pair</item>
		///     <item>Combining character sequence</item>
		///     <item>Variation sequence (including IVS)</item>
		///   </list>
		/// </remarks>
		/// <seealso cref="Document.PrevGraphemeClusterIndex"/>
		/// <seealso cref="Document.IsNotDividableIndex(int)"/>
		public int NextGraphemeClusterIndex( int index )
		{
			if( index < 0 || Length < index )
				throw new ArgumentOutOfRangeException( "index" );

			return TextUtil.NextGraphemeClusterIndex( _Buffer, index );
		}

		/// <summary>
		/// Gets index of previous grapheme cluster.
		/// </summary>
		/// <param name="index">The index to start the search from.</param>
		/// <returns>The index of the character which starts previous grapheme cluster.</returns>
		/// <exception cref="System.ArgumentOutOfRangeException">Parameter '<paramref name="index"/>' is out of valid range.</exception>
		/// <remarks>
		///   <para>
		///   This method searches document for a grapheme cluster
		///   from given <paramref name="index"/> backward.
		///   Note that this method always return an index less than given '<paramref name="index"/>'.
		///   </para>
		///   <para>
		///   'Grapheme cluster' is a sequence of characters
		///   which consists one 'user perceived character'
		///   such as sequence of U+0041 and U+0300; a capital 'A' with grave (&#x0041;&#x0300;).
		///   In most cases, such sequence should not be divided unless user wishes to do so.
		///   </para>
		///   <para>
		///   This method determines an index pointing the middle of character sequences next as undividable:
		///   </para>
		///   <list type="bullet">
		///     <item>CR+LF</item>
		///     <item>Surrogate pair</item>
		///     <item>Combining character sequence</item>
		///     <item>Variation sequence (including IVS)</item>
		///   </list>
		/// </remarks>
		/// <seealso cref="Document.PrevGraphemeClusterIndex"/>
		/// <seealso cref="Document.IsNotDividableIndex(int)"/>
		public int PrevGraphemeClusterIndex( int index )
		{
			if( index < 0 || Length < index )
				throw new ArgumentOutOfRangeException( "index" );

			return TextUtil.PrevGraphemeClusterIndex( _Buffer, index );
		}

		/// <summary>
		/// Gets content enumerator.
		/// </summary>
		IEnumerator IEnumerable.GetEnumerator()
		{
			return _Buffer.GetEnumerator();
		}

		/// <summary>
		/// Gets content enumerator.
		/// </summary>
		public IEnumerator<char> GetEnumerator()
		{
			return _Buffer.GetEnumerator();
		}

		/// <summary>
		/// Gets one character at given index.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException"/>
		public char this[ int index ]
		{
			get
			{
				if( index < 0 || _Buffer.Count <= index )
					throw new ArgumentOutOfRangeException( "index", "Invalid index was given"
														   + " (index:" + index + ", Length:"
														   + Length + ")." );

				return _Buffer[index];
			}
		}

		/// <summary>
		/// Determines whether text can not be divided at given index or not.
		/// </summary>
		/// <param name="index">
		///   The index to determine whether it points to middle of an undividable character
		///   sequence or not.
		/// </param>
		/// <returns>Whether charcter sequence can not be divided at the index or not.</returns>
		/// <remarks>
		///   <para>
		///   This method determines whether text can not be divided at given index or not. To seek
		///   document through grapheme cluster by grapheme cluster, please consider to use
		///   <see cref="Document.NextGraphemeClusterIndex">
		///   Document.NextGraphemeClusterIndex method</see> or
		///   <see cref="Document.PrevGraphemeClusterIndex">
		///   Document.PrevGraphemeClusterIndex method</see>.
		///   </para>
		///   <para>
		///   This method determines an index pointing the middle of character sequences next as
		///   undividable:
		///   </para>
		///   <para>
		///   'Grapheme cluster' is a sequence of characters which consists one 'user perceived
		///   character' such as sequence of U+0041 and U+0300; a capital 'A' with grave
		///   (&#x0041;&#x0300;). In most cases, such sequence should not be divided unless user
		///   wishes to do so.
		///   </para>
		///   <list type="bullet">
		///     <item>CR+LF</item>
		///     <item>Surrogate pair</item>
		///     <item>Combining character sequence</item>
		///     <item>Variation sequence (including IVS)</item>
		///   </list>
		/// </remarks>
		/// <seealso cref="Document.NextGraphemeClusterIndex"/>
		/// <seealso cref="Document.PrevGraphemeClusterIndex"/>
		public bool IsNotDividableIndex( int index )
		{
			if( index <= 0 || Length <= index )
				return false;

			return TextUtil.IsNotDividableIndex( this[index-1],
												 this[index],
												 (index+1 < Length) ? this[index+1]
																	: '\0' );
		}

		/// <summary>
		/// Determines whether given character is a combining character or not.
		/// </summary>
		public bool IsCombiningCharacter( int index )
		{
			if( index < 0 || Length <= index )
				return false;

			return TextUtil.IsCombiningCharacter( this[index] );
		}

		/// <summary>
		/// Determines whether given character(s) is a variation selector or not.
		/// </summary>
		public bool IsVariationSelector( int index )
		{
			if( index < 0 || Length <= index+1 )
				return false;

			return TextUtil.IsVariationSelector( this[index], this[index+1] );
		}

		internal void DeleteRectSelectText()
		{
			int diff = 0;

			for( int i=0; i<RectSelectRanges.Length; i+=2 )
			{
				// recalculate range of this row
				RectSelectRanges[i] -= diff;
				RectSelectRanges[i+1] -= diff;

				// replace this row
				Debug.Assert( IsNotDividableIndex(RectSelectRanges[i]) == false );
				Debug.Assert( IsNotDividableIndex(RectSelectRanges[i+1]) == false );
				Replace( String.Empty, RectSelectRanges[i], RectSelectRanges[i+1] );

				// go to next row
				diff += RectSelectRanges[i+1] - RectSelectRanges[i];
			}

			// reset selection
			SetSelection( RectSelectRanges[0], RectSelectRanges[0] );
		}

		internal void GetSelectedLineRange( out int selBeginL,
											out int selEndL )
		{
			int selBegin, selEnd;

			GetSelection( out selBegin, out selEnd );
			selBeginL = Lines.AtOffset( selBegin ).LineIndex;
			selEndL = Lines.AtOffset( selEnd ).LineIndex;
			if( selBeginL == selEndL
				|| Lines[selEndL].Begin != selEnd )
			{
				selEndL++; // Target the final line too unless multiple lines
						   // are selected and at least one char is selected
			}
		}

		bool IsEmptyLine( int index )
		{
			// is the index indicates end of the document or end of a line?
			if( index == Length
				|| index < Length && TextUtil.IsEolChar(this[index]))
			{
				// is the index indicates start of the document or start of a line?
				if( index == 0
					|| 0 <= index-1 && TextUtil.IsEolChar(this[index-1]) )
				{
					return true;
				}
			}
			return false;
		}
		#endregion
	}

	#region Types for Events
	/// <summary>
	/// Event handler for SelectionChanged event.
	/// </summary>
	public delegate void SelectionChangedEventHandler( object sender, SelectionChangedEventArgs e );

	/// <summary>
	/// Event information about selection change.
	/// </summary>
	public class SelectionChangedEventArgs : EventArgs
	{
		readonly int _OldAnchor;
		readonly int _OldCaret;
		readonly int[] _OldRectSelectRanges;
		readonly bool _ByContentChanged;

		/// <summary>
		/// Creates a new instance.
		/// </summary>
		public SelectionChangedEventArgs( int anchorIndex, int caretIndex, int[] oldRectSelectRanges, bool byContentChanged )
		{
			_OldAnchor = anchorIndex;
			_OldCaret = caretIndex;
			_OldRectSelectRanges = oldRectSelectRanges;
			_ByContentChanged = byContentChanged;
		}

		/// <summary>
		/// Anchor index (in current text) of the previous selection.
		/// </summary>
		public int OldAnchor
		{
			get{ return _OldAnchor; }
		}

		/// <summary>
		/// Caret index (in current text) of the previous selection.
		/// </summary>
		public int OldCaret
		{
			get{ return _OldCaret; }
		}

		/// <summary>
		/// Text ranges selected by previous rectangle selection (indexes are valid in current text.)
		/// </summary>
		public int[] OldRectSelectRanges
		{
			get{ return _OldRectSelectRanges; }
		}

		/// <summary>
		/// This value will be true if this event has been occured because the document was modified.
		/// </summary>
		public bool ByContentChanged
		{
			get{ return _ByContentChanged; }
		}
	}

	/// <summary>
	/// Event handler for ContentChanged event.
	/// </summary>
	public delegate void ContentChangedEventHandler( object sender, ContentChangedEventArgs e );

	/// <summary>
	/// Event information about content change.
	/// </summary>
	public class ContentChangedEventArgs : EventArgs
	{
		readonly int _Index;
		readonly string _OldText, _NewText;

		/// <summary>
		/// Creates a new instance.
		/// </summary>
		public ContentChangedEventArgs( int index, string oldText, string newText )
		{
			_Index = index;
			_OldText = oldText;
			_NewText = newText;
		}

		/// <summary>
		/// Gets index of the position where the replacement occured.
		/// </summary>
		public int Index
		{
			get{ return _Index; }
		}

		/// <summary>
		/// Gets replaced text.
		/// </summary>
		public string OldText
		{
			get{ return _OldText; }
		}

		/// <summary>
		/// Gets newly inserted text.
		/// </summary>
		public string NewText
		{
			get{ return _NewText; }
		}

		/// <summary>
		/// Gets or sets starting index of the range to be redrawn after this event.
		/// </summary>
		public int RedrawStartIndex { get; set; }

		/// <summary>
		/// Gets or sets ending index of the range to be redrawn after this event.
		/// </summary>
		public int RedrawEndIndex { get; set; }
	}
	#endregion
}
