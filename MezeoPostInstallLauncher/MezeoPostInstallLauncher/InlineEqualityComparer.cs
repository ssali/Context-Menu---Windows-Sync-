using System;
using System.Collections.Generic;
using System.Linq;

namespace MezeoPostInstallLauncher
{
    /// <summary>
    /// Found at http://www.lexparse.com/2009/11/02/c-lambdas-never-implement-icomparer-and-iequalitycomparer-again/
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class InlineEqualityComparer<T> : IEqualityComparer<T>
    {
        private Func<T, T, bool> _equalsFn;
        private Func<T, int> _getHashCodefn;

        public static InlineEqualityComparer<T> Create(Func<T, T, bool> equalsFn, Func<T, int> getHashCodefn)
        {
            return new InlineEqualityComparer<T>(equalsFn, getHashCodefn);
        }

        public static InlineEqualityComparer<T> CreateDefault()
        {
            return InlineEqualityComparer<T>.Create((item1, item2) => item1.Equals(item2), item => item.GetHashCode());
        }

        private InlineEqualityComparer(Func<T, T, bool> equalsFn, Func<T, int> getHashCodefn)
        {
            _equalsFn = equalsFn;
            _getHashCodefn = getHashCodefn;
        }

        public bool Equals(T x, T y)
        {
            return _equalsFn(x, y);
        }

        public int GetHashCode(T obj)
        {
            return _getHashCodefn(obj);
        }
    }
}
