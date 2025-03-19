﻿using System.Threading.Tasks;

namespace BigRedProf.Stories
{
	// cool idea from
	// https://medium.com/@a.lyskawa/the-hitchhiker-guide-to-asynchronous-events-in-c-e9840109fb53
	public delegate Task AsyncEventHandler<TEventArgs>(object? sender, TEventArgs e);
}
