//------------------------------------------------------------
//        File:  StackPool.cs
//       Brief:  StackPool
//
//      Author:  VyronLee, lwz_jz@hotmail.com
//
//    Modified:  2019-07-09 19:44
//   Copyright:  Copyright (c) 2019, VyronLee
//============================================================

using System.Collections.Generic;

namespace vFrame.Bundler.Base.Pools
{
    internal class StackPool<T> : ObjectPool<Stack<T>, StackAllocator<T>>
    {
    }
}