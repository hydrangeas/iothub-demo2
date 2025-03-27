using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace MachineLog.Common.Synchronization
{
    /// <summary>
    /// スレッドセーフなHashSetの実装
    /// </summary>
    /// <typeparam name="T">要素の型</typeparam>
    public class ConcurrentHashSet<T> : ICollection<T>, IReadOnlyCollection<T>
    {
        private readonly ConcurrentDictionary<T, byte> _dictionary;

        /// <summary>
        /// ConcurrentHashSetを初期化します
        /// </summary>
        public ConcurrentHashSet()
        {
            _dictionary = new ConcurrentDictionary<T, byte>();
        }

        /// <summary>
        /// ConcurrentHashSetを初期化します
        /// </summary>
        /// <param name="collection">初期コレクション</param>
        public ConcurrentHashSet(IEnumerable<T> collection)
        {
            _dictionary = new ConcurrentDictionary<T, byte>(
                collection.Select(x => new KeyValuePair<T, byte>(x, 0)));
        }

        /// <summary>
        /// ConcurrentHashSetを初期化します
        /// </summary>
        /// <param name="comparer">要素の比較に使用するIEqualityComparer</param>
        public ConcurrentHashSet(IEqualityComparer<T> comparer)
        {
            _dictionary = new ConcurrentDictionary<T, byte>(comparer);
        }

        /// <summary>
        /// ConcurrentHashSetを初期化します
        /// </summary>
        /// <param name="collection">初期コレクション</param>
        /// <param name="comparer">要素の比較に使用するIEqualityComparer</param>
        public ConcurrentHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            _dictionary = new ConcurrentDictionary<T, byte>(
                collection.Select(x => new KeyValuePair<T, byte>(x, 0)), comparer);
        }

        /// <summary>
        /// 要素を追加します
        /// </summary>
        /// <param name="item">追加する要素</param>
        /// <returns>要素が追加された場合はtrue、既に存在する場合はfalse</returns>
        public bool Add(T item)
        {
            return _dictionary.TryAdd(item, 0);
        }

        /// <summary>
        /// 要素を追加します
        /// </summary>
        /// <param name="item">追加する要素</param>
        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        /// <summary>
        /// すべての要素を削除します
        /// </summary>
        public void Clear()
        {
            _dictionary.Clear();
        }

        /// <summary>
        /// 指定した要素が含まれているかどうかを判断します
        /// </summary>
        /// <param name="item">検索する要素</param>
        /// <returns>要素が含まれている場合はtrue</returns>
        public bool Contains(T item)
        {
            return _dictionary.ContainsKey(item);
        }

        /// <summary>
        /// 要素を配列にコピーします
        /// </summary>
        /// <param name="array">コピー先の配列</param>
        /// <param name="arrayIndex">コピー開始インデックス</param>
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < Count) throw new ArgumentException("配列のサイズが不足しています");

            var i = arrayIndex;
            foreach (var key in _dictionary.Keys)
            {
                array[i++] = key;
            }
        }

        /// <summary>
        /// 要素を削除します
        /// </summary>
        /// <param name="item">削除する要素</param>
        /// <returns>要素が削除された場合はtrue</returns>
        public bool Remove(T item)
        {
            return _dictionary.TryRemove(item, out _);
        }

        /// <summary>
        /// 要素の数を取得します
        /// </summary>
        public int Count => _dictionary.Count;

        /// <summary>
        /// コレクションが読み取り専用かどうかを取得します
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// 列挙子を取得します
        /// </summary>
        /// <returns>列挙子</returns>
        public IEnumerator<T> GetEnumerator()
        {
            return _dictionary.Keys.GetEnumerator();
        }

        /// <summary>
        /// 列挙子を取得します
        /// </summary>
        /// <returns>列挙子</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// 指定したコレクションのすべての要素を追加します
        /// </summary>
        /// <param name="collection">追加するコレクション</param>
        public void UnionWith(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            foreach (var item in collection)
            {
                Add(item);
            }
        }

        /// <summary>
        /// 指定したコレクションに含まれない要素のみを残します
        /// </summary>
        /// <param name="collection">比較するコレクション</param>
        public void ExceptWith(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            foreach (var item in collection)
            {
                Remove(item);
            }
        }

        /// <summary>
        /// 指定したコレクションと共通する要素のみを残します
        /// </summary>
        /// <param name="collection">比較するコレクション</param>
        public void IntersectWith(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            var hashSet = new HashSet<T>(collection);
            var keysToRemove = _dictionary.Keys.Where(key => !hashSet.Contains(key)).ToList();

            foreach (var key in keysToRemove)
            {
                Remove(key);
            }
        }
    }
}