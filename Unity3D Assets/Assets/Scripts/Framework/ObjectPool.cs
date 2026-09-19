using System.Collections.Generic;

namespace DedicatedServer.Framework
{
    public class ObjectPool<T> where T : IObjectPoolItem, new()
    {
        private Queue<T> _pool;

        public ObjectPool()
        {
            this._pool = new Queue<T>();
        }

        public T Get()
        {
            T obj;
            if(this._pool.TryDequeue(out obj))
            {
                return obj;
            }
            return new T();
        }

        public void DisposeIntoObjectPool(T obj)
        {
            this._pool.Enqueue(obj);
        }
    }

    public interface IObjectPoolItem
    {
    }
}
