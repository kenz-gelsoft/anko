﻿namespace Sgry.Azuki
{
	/// <summary>
	/// Range of a text line.
	/// </summary>
	public interface ILineRange : IRange
	{
		/// <summary>
		/// Gets EOL code which terminates this line.
		/// </summary>
		string EolCode{ get; }

		/// <summary>
		/// Gets or sets dirty state of this line.
		/// </summary>
		DirtyState DirtyState
		{
			get; set;
		}
	}
}