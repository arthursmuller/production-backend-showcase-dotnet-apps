using System;

namespace StarterEntities
{
    public abstract class BaseEntity
    {
        protected int _id;
        public virtual int Id => _id;
        public DateTime CreatedDate => _createdDate;
        protected DateTime _createdDate;
        public DateTime UpdateDate { get; set; } = DateTime.UtcNow;
        public string UserUpdate { get; set; } = "SISTEMA";
        public BaseEntity() {
            _createdDate  = DateTime.UtcNow;
        }

        public BaseEntity(DateTime createdDate)
        {
            _createdDate = createdDate;
        }

        public BaseEntity(int id)
        {
            _id = id;
        }
    }
}
