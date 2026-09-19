using DedicatedServer.Framework.Networking;
using System;
using System.Collections;
using System.Collections.Generic;

namespace DedicatedServer.Framework.ECS
{
    public class EntityDataContainer
    {
        private bool _isDirty;
        public bool IsDirty => this._isDirty;
        private DictionaryList<ushort, EntityDataStoreBase> _data;

        public EntityDataContainer()
        {
            this._data = new DictionaryList<ushort, EntityDataStoreBase>();
        }

        public void MarkDirty()
        {
            this._isDirty = true;
        }

        public EntityDataContainer CreateCopy()
        {
            EntityDataContainer copy = EntityDataContainer.Create(this.GetDataStoreTypes());
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(EntityDataContainer source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (this._data.Count != source._data.Count)
            {
                throw new InvalidOperationException("Cannot copy entity data containers with different numbers of data stores.");
            }

            foreach (EntityDataStoreBase sourceDataStore in source._data)
            {
                EntityDataStoreBase targetDataStore = this._data.Get(sourceDataStore.Index);
                if (targetDataStore is null)
                {
                    throw new InvalidOperationException("Cannot copy entity data container because a data store is missing in the target entity data container.");
                }
                targetDataStore.CopyFrom(sourceDataStore);
            }

            this._isDirty = source._isDirty;
        }

        public bool WriteToPacket(SentPacket packet, bool writeFullData)
        {
            int numberOfDataStoreIndex = packet.NextWriteIndex;
            ushort numberOfDataStore = 0;
            packet.WriteUInt16(numberOfDataStore);
            foreach(EntityDataStoreBase data in this._data)
            {
                if (data.IsDirty || writeFullData)
                {
                    data.WriteToPacket(packet, writeFullData);
                    numberOfDataStore++;
                }
            }
            int finalWriteIndex = packet.NextWriteIndex;
            packet.SetNextWriteIndex(numberOfDataStoreIndex);
            packet.WriteUInt16(numberOfDataStore);
            packet.SetNextWriteIndex(finalWriteIndex);
            this._isDirty = false;
            return numberOfDataStore > 0;
        }

        public ushort ReadFromPacket(ReceivedPacket packet, bool markDirty)
        {
            ushort numberOfDataStore = packet.ReadUInt16();
            for (int i = 0; i < numberOfDataStore; i++)
            {
                ushort dataIndex = packet.ReadUInt16();
                EntityDataStoreBase data = this._data.Get(dataIndex);
                data.ReadFromPacket(packet, markDirty);
                if (markDirty && !this._isDirty && data.IsDirty)
                {
                    this._isDirty = true;
                }
            }
            return numberOfDataStore;
        }

        public EntityDataStoreBase GetEntityDataStore(ushort index)
        {
            return this._data.Get(index);
        }

        public T GetEntityDataStore<T>() where T : EntityDataStoreBase
        {
            foreach(EntityDataStoreBase data in this._data)
            {
                if(data is T)
                {
                    return (T)data;
                }
            }
            return null;
        }

        private IEnumerable<Type> GetDataStoreTypes()
        {
            foreach (EntityDataStoreBase dataStore in this._data)
            {
                yield return dataStore.GetType();
            }
        }

        public static EntityDataContainer Create(IEnumerable<Type> dataStoreTypes)
        {
            EntityDataContainer result = new EntityDataContainer();
            foreach (Type dataStoreType in dataStoreTypes)
            {
                EntityDataStoreBase dataStore = (EntityDataStoreBase)Activator.CreateInstance(dataStoreType);
                dataStore.SetEntityDataContainer(result);
                result._data.Add(dataStore.Index, dataStore);
            }
            return result;
        }

        public static EntityDataContainer Create(IEnumerator dataStoreTypes)
        {
            List<Type> dataStoreTypeList = new List<Type>();
            while (dataStoreTypes.MoveNext())
            {
                dataStoreTypeList.Add((Type)dataStoreTypes.Current);
            }
            return EntityDataContainer.Create(dataStoreTypeList);
        }
    }
}
