﻿namespace Sgry.Azuki
{
	public interface IRangeList
	{
		IRange this[ int lineIndex ]
		{
			get;
		}

		int Count
		{
			get;
		}
	}
}
