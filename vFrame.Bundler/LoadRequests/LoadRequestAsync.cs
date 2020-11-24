﻿//------------------------------------------------------------
//        File:  LoadRequestAsync.cs
//       Brief:  LoadRequestAsync
//
//      Author:  VyronLee, lwz_jz@hotmail.com
//
//    Modified:  2019-02-15 20:50
//   Copyright:  Copyright (c) 2019, VyronLee
//============================================================

using System.Collections;
using System.Collections.Generic;
using vFrame.Bundler.Base.Pools;
using vFrame.Bundler.Interface;
using vFrame.Bundler.Loaders;
using vFrame.Bundler.Modes;

namespace vFrame.Bundler.LoadRequests
{
    public sealed class LoadRequestAsync : LoadRequest, ILoadRequestAsync
    {
        private class Node
        {
            public BundleLoaderBase Loader;
            public readonly List<Node> Children = new List<Node>();
        }

        private Node _root;
        private int _total;

        public LoadRequestAsync(ModeBase mode, BundlerOptions options, string path, BundleLoaderBase bundleLoader)
            : base(mode, options, path, bundleLoader) {
        }

        public IEnumerator Await() {
            if (_finished)
                yield break;

            if (!IsStarted)
                _root.Loader.Retain();
            IsStarted = true;

            yield return TravelAndLoadLeaf(_root);

            if (!_finished)
                _root.Loader.Release();
            _finished = true;
        }

        public bool IsStarted { get; private set; }

        public float Progress {
            get {
                var progress = CalculateLoadingProgress();
                return progress / _total;
            }
        }

        protected override void LoadInternal() {
            _root = BuildLoaderTree(ref _total);
        }

        private Node BuildLoaderTree(ref int count) {
            var root = new Node {
                Loader = _bundleLoader
            };
            BuildTree(root, _bundleLoader.Dependencies, ref count);
            count += 1;
            return root;
        }

        private static void BuildTree(Node parent, IEnumerable<BundleLoaderBase> children, ref int count) {
            foreach (var child in children) {
                var node = new Node {
                    Loader = child
                };
                BuildTree(node, child.Dependencies, ref count);
                count += 1;

                parent.Children.Add(node);
            }
        }

        private IEnumerator TravelAndLoadLeaf(Node node) {
            foreach (var child in node.Children) {
                // Run child loader parallel.
                var enumerators = ListPool<IEnumerator>.Get();
                enumerators.Add(TravelAndLoadLeaf(child));
                foreach (var enumerator in enumerators) {
                    yield return enumerator;
                }
                ListPool<IEnumerator>.Return(enumerators);
            }

            var loader = node.Loader;

            if (loader.IsDone)
                yield break;

            if (!loader.IsStarted) {
                loader.Load();

                var async = node.Loader as BundleLoaderAsync;
                if (async != null) {
                    yield return async.Await();
                }
            }

            while (loader.IsLoading) {
                yield return null;
            }
        }

        private float CalculateLoadingProgress() {
            var progress = 0f;
            CalculateNodeProgress(_root, ref progress);
            return progress;
        }

        private void CalculateNodeProgress(Node node, ref float progress) {
            foreach (var child in node.Children)
                CalculateNodeProgress(child, ref progress);

            var loaderAsync = node.Loader as BundleLoaderAsync;
            if (null != loaderAsync)
                progress += loaderAsync.Progress;
            else
                progress += 1;
        }
    }
}