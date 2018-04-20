# Unity Heap Crawler

Customizable heap snapshotting tool for Unity game engine. Can be used to detect memory leaks and analyze high heap usage.

## Features
1. Reflection-based
2. Results are plain text (see [output overview](snapshot-2018-04-20T18_31_38/))
3. Human readable results (objects are traversed using [BFS](https://en.wikipedia.org/wiki/Breadth-first_search))
4. Little memory overhead - most crawling data is discared after aggregation
5. Highly customizable - choose between fullness and low memory usage (see [documentation](Docs/html/class_unity_heap_crawler_1_1_heap_snapshot_collector.html))
6. References to destroyed Unity objects that still take heap space are clearly visible
7. Unity editor is not needed. You can make a snapshot in build

## Motivation

When heap consumption and memory leaks became problems in our project I could not find a tool that could make a mono heap snapshot to help me find those leaks.
* Builtin Memory Profiler (Profiler window in Editor) is good for analyzing native resources but provides only heap size without any details
* [Unity Memory Profiler](https://bitbucket.org/Unity-Technologies/memoryprofiler) does not collect heap objects on mono runtime (even though patch notes state it does in 2017.1). Also, taking snapshot in our project used up 32GB RAM and that is _without_ heap objects.
* There is no access to mono runtime in Unity so [mono HeapShot](http://www.mono-project.com/archived/heapshot/) is not an option

Current solution relies heavily on ideas and memory estimation code from previous reflection based crawlers - my collegue's [UnityHeapEx](https://github.com/Cotoff/UnityHeapEx) and [UnityHeapDump](https://github.com/Zuntatos/UnityHeapDump) by [Zuntatos](https://github.com/Zuntatos). I could not use them as is due high memory consumption (all references data won't fit in memory) and low results readability.

## Issues
* Static fields in generic types and not detected. User can supply those Type objects manually
* Type memory usage is an estimation and can be slightly off

## Authors
* [**Vasily Boldyrev**](https://github.com/vasyab) - _Owlcat Games_

## Credits
* [Cotoff](https://github.com/Cotoff) for original [UnityHeapEx](https://github.com/Cotoff/UnityHeapEx)
* [Zuntatos](https://github.com/Zuntatos) for another implementation of the same idea [UnityHeapDump](https://github.com/Zuntatos/UnityHeapDump)

## Licence

This code is distributed under the terms and conditions of the MIT license.
