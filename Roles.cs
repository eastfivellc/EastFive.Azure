//using System;
//using System.Net.Http;
//using System.Threading.Tasks;
//using System.Linq;

//namespace EastFive.Security.SessionServer
//{
//    public struct Role
//    {
//        public Guid id;
//        public Guid actorId;
//        public string name;
//    }

//    public class Roles
//    {
//        public async Task<TResult> CreateAsync<TResult>(Guid id, Guid actorId, string name,
//            Func<TResult> success, 
//            Func<TResult> alreadyExists,
//            Func<TResult> onActorNotFound)
//        {
//            return await dataContext.Roles.CreateAsync(id, actorId, name,
//                 success,
//                 alreadyExists,
//                 onActorNotFound);
//        }
        
//        public async Task<T> GetByIdAsync<T>(Guid id,
//            Func<Role, T> callback,
//            Func<T> notFound)
//        {
//            return await dataContext.Roles.FindByIdAsync(id,
//                (role) => callback(Convert(role)),
//                notFound);
//        }

//        public async Task<T> GetByActorIdAsync<T>(Guid actorId,
//            Func<Role[], T> callback,
//            Func<T> notFound)
//        {
//            return await dataContext.Roles.FindByActorIdAsync(actorId,
//                (roles) => callback(roles.Select(Convert).ToArray()),
//                notFound);
//        }
        
//        public Task<TResult> UpdateAsync<TResult>(Guid contactId, 
//            Guid? actorId, string name,
//            Func<TResult> success,
//            Func<string, TResult> failed,
//            Func<TResult> notFound)
//        {
//            var result = dataContext.Roles.UpdateAsync(contactId,
//                async (current, save) =>
//                {
//                    if (actorId.HasValue && actorId.Value != current.actorId)
//                        return failed("Cannot modify Actor. Please delete and recreate.");
//                    await save(name);
//                    return success();
//                },
//                () => notFound());
//            return result;
//        }

//        private static Role Convert(Persistence.Role role)
//        {
//            return new Role
//            {
//                id = role.id,
//                actorId = role.actorId,
//                name = role.name,
//            };
//        }

//        public async Task<TResult> DeleteByIdAsync<TResult>(Guid roleId,
//            Func<TResult> onSuccess,
//            Func<TResult> onNotFound)
//        {
//            return await dataContext.Roles.DeleteByIdAsync(roleId,
//                onSuccess,
//                onNotFound);
//        }
//    }
//}