//using System;
//using System.Threading.Tasks;
//using BlackBarLabs.Persistence.Azure.StorageTables;
//using BlackBarLabs.Persistence;
//using BlackBarLabs.Persistence.Azure;
//using System.Linq;

//namespace EastFive.Security.SessionServer.Persistence
//{
//    public struct Role
//    {
//        public Guid id;
//        public Guid actorId;
//        public string name;
//    }

//    //public class Roles
//    //{
//    //    private readonly AzureStorageRepository azureStorageRepository;
//    //    private DataContext dataContext;
        
//    //    internal Roles(DataContext dataContext)
//    //    {
//    //        this.dataContext = dataContext;
//    //        this.azureStorageRepository = dataContext.AzureStorageRepository;
//    //    }

//    //    public async Task<TResult> CreateAsync<TResult>(
//    //        Guid id, Guid actorId, string name, 
//    //        Func<TResult> onSuccess,
//    //        Func<TResult> onAlreadyExists,
//    //        Func<TResult> onActorNotFound)
//    //    {
//    //        var rollback = new RollbackAsync<TResult>();
//    //        var document = new Documents.RoleDocument()
//    //        {
//    //            Name = name,
//    //            ActorId = actorId,
//    //        };
//    //        rollback.AddTaskCreate(id, document, onAlreadyExists, this.azureStorageRepository);

//    //        rollback.AddTaskCreateOrUpdate(actorId,
//    //            (Documents.ActorMappingsDocument actorDoc) =>
//    //            {
//    //                actorDoc.AddRole(id);
//    //                return true;
//    //            },
//    //            (actorDoc) => actorDoc.RemoveRole(id),
//    //            onActorNotFound,
//    //            this.azureStorageRepository);

//    //        return await rollback.ExecuteAsync(onSuccess);
//    //    }

//    //    public async Task<T> FindByIdAsync<T>(Guid id,
//    //        Func<Role, T> found,
//    //        Func<T> notFound)
//    //    {
//    //        var result = await this.azureStorageRepository.FindByIdAsync(id,
//    //            (Documents.RoleDocument doc) =>found(Convert(doc)),
//    //            () => notFound());
//    //        return result;
//    //    }

//    //    public async Task<T> FindByActorIdAsync<T>(Guid actorId,
//    //        Func<Role[], T> found,
//    //        Func<T> notFound)
//    //    {
//    //        var result = await this.azureStorageRepository.FindLinkedDocumentsAsync(actorId,
//    //            (doc) => doc.GetRoles(),
//    //            (Documents.ActorMappingsDocument actorDoc, Documents.RoleDocument[] roleDocs) => 
//    //                found(roleDocs.Select(Convert).ToArray()),
//    //            () => notFound());
//    //        return result;
//    //    }

//    //    public async Task<TResult> UpdateAsync<TResult>(Guid roleId, 
//    //        Func<Role, Func<string, Task>, Task<TResult>> found,
//    //        Func<TResult> notFound)
//    //    {
//    //        return await this.azureStorageRepository.UpdateAsync<Documents.RoleDocument, TResult>(roleId,
//    //            async (current, save) =>
//    //            {
//    //                return await found(Convert(current),
//    //                    async (nameNew) =>
//    //                    {
//    //                        current.Name = nameNew;
//    //                        await save(current);
//    //                    });
//    //            },
//    //            () => notFound());
//    //    }

//    //    public Task<TResult> DeleteByIdAsync<TResult>(Guid roleId, 
//    //        Func<TResult> onSuccess, 
//    //        Func<TResult> onNotFound)
//    //    {
//    //        return this.azureStorageRepository.DeleteIfAsync<Documents.RoleDocument, TResult>(roleId,
//    //            async (doc, delete) =>
//    //            {
//    //                await delete();
//    //                return onSuccess();
//    //            },
//    //            onNotFound);
//    //    }

//    //    private Role Convert(Documents.RoleDocument doc)
//    //    {
//    //        return new Role
//    //        {
//    //            id = doc.Id,
//    //            name = doc.Name,
//    //            actorId = doc.ActorId,
//    //        };
//    //    }
//    //}
//}
