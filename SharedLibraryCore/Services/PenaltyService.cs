﻿using Microsoft.EntityFrameworkCore;
using SharedLibraryCore.Database;
using SharedLibraryCore.Database.Models;
using SharedLibraryCore.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace SharedLibraryCore.Services
{
    public class PenaltyService : Interfaces.IEntityService<EFPenalty>
    {
        public virtual async Task<EFPenalty> Create(EFPenalty newEntity)
        {
            using (var context = new DatabaseContext())
            {
                var penalty = new EFPenalty()
                {
                    Active = true,
                    OffenderId = newEntity.Offender.ClientId,
                    PunisherId = newEntity.Punisher.ClientId,
                    LinkId = newEntity.Link.AliasLinkId,
                    Type = newEntity.Type,
                    Expires = newEntity.Expires,
                    Offense = newEntity.Offense,
                    When = DateTime.UtcNow,
                    AutomatedOffense = newEntity.AutomatedOffense ?? newEntity.Punisher.AdministeredPenalties?.FirstOrDefault()?.AutomatedOffense,
                    IsEvadedOffense = newEntity.IsEvadedOffense
                };

                context.Penalties.Add(penalty);
                await context.SaveChangesAsync();
            }

            return newEntity;
        }

        public Task<EFPenalty> Delete(EFPenalty entity)
        {
            throw new NotImplementedException();
        }

        public async Task<IList<EFPenalty>> Find(Func<EFPenalty, bool> expression)
        {
            throw await Task.FromResult(new Exception());
        }

        public Task<EFPenalty> Get(int entityID)
        {
            throw new NotImplementedException();
        }

        public Task<EFPenalty> GetUnique(long entityProperty)
        {
            throw new NotImplementedException();
        }

        public Task<EFPenalty> Update(EFPenalty entity)
        {
            throw new NotImplementedException();
        }

        public async Task<IList<PenaltyInfo>> GetRecentPenalties(int count, int offset, EFPenalty.PenaltyType showOnly = EFPenalty.PenaltyType.Any, bool ignoreAutomated = true)
        {
            using (var context = new DatabaseContext(true))
            {
                var iqPenalties = context.Penalties
                    .Where(p => showOnly == EFPenalty.PenaltyType.Any ? p.Type != EFPenalty.PenaltyType.Any : p.Type == showOnly)
                    .Where(_penalty => ignoreAutomated ? _penalty.PunisherId != 1 : true)
                    .OrderByDescending(p => p.When)
                    .Skip(offset)
                    .Take(count)
                     .Select(_penalty => new PenaltyInfo()
                     {
                         Id = _penalty.PenaltyId,
                         Offense = _penalty.Offense,
                         AutomatedOffense = _penalty.AutomatedOffense,
                         OffenderId = _penalty.OffenderId,
                         OffenderName = _penalty.Offender.CurrentAlias.Name,
                         PunisherId = _penalty.PunisherId,
                         PunisherName = _penalty.Punisher.CurrentAlias.Name,
                         PunisherLevel = _penalty.Punisher.Level,
                         PenaltyType = _penalty.Type,
                         Expires = _penalty.Expires,
                         TimePunished = _penalty.When,
                         IsEvade = _penalty.IsEvadedOffense
                     });

                return await iqPenalties.ToListAsync();
            }
        }

        /// <summary>
        /// retrieves penalty information for meta service
        /// </summary>
        /// <param name="clientId">database id of the client</param>
        /// <param name="count">how many items to retrieve</param>
        /// <param name="offset">not used</param>
        /// <param name="startAt">retreive penalties older than this</param>
        /// <returns></returns>
        public async Task<IList<PenaltyInfo>> GetClientPenaltyForMetaAsync(int clientId, int count, int offset, DateTime? startAt)
        {
            var linkedPenaltyType = Utilities.LinkedPenaltyTypes();

            using (var ctx = new DatabaseContext(true))
            {
                var linkId = await ctx.Clients.AsNoTracking()
                    .Where(_penalty => _penalty.ClientId == clientId)
                    .Select(_penalty => _penalty.AliasLinkId)
                    .FirstOrDefaultAsync();

                var iqPenalties = ctx.Penalties.AsNoTracking()
                    .Where(_penalty => _penalty.OffenderId == clientId || _penalty.PunisherId == clientId || (linkedPenaltyType.Contains(_penalty.Type) && _penalty.LinkId == linkId))
                    .Where(_penalty => _penalty.When <= startAt)
                    .OrderByDescending(_penalty => _penalty.When)
                    .Skip(offset)
                    .Take(count)
                    .Select(_penalty => new PenaltyInfo()
                    {
                        Id = _penalty.PenaltyId,
                        Offense = _penalty.Offense,
                        AutomatedOffense = _penalty.AutomatedOffense,
                        OffenderId = _penalty.OffenderId,
                        OffenderName = _penalty.Offender.CurrentAlias.Name,
                        PunisherId = _penalty.PunisherId,
                        PunisherName = _penalty.Punisher.CurrentAlias.Name,
                        PunisherLevel = _penalty.Punisher.Level,
                        PenaltyType = _penalty.Type,
                        Expires = _penalty.Expires,
                        TimePunished = _penalty.When,
                        IsEvade = _penalty.IsEvadedOffense
                    });

                return await iqPenalties.Distinct().ToListAsync();
            }
        }

        public async Task<List<EFPenalty>> GetActivePenaltiesAsync(int linkId, int? ip = null, bool includePunisherName = false)
        {
            var now = DateTime.UtcNow;

            Expression<Func<EFPenalty, bool>> filter = (p) => (new EFPenalty.PenaltyType[]
                         {
                            EFPenalty.PenaltyType.TempBan,
                            EFPenalty.PenaltyType.Ban,
                            EFPenalty.PenaltyType.Flag
                         }.Contains(p.Type) &&
                         p.Active &&
                         (p.Expires == null || p.Expires > now));

            using (var context = new DatabaseContext(true))
            {
                var iqLinkPenalties = context.Penalties
                    .Where(p => p.LinkId == linkId)
                    .Where(filter);

                var iqIPPenalties = context.Aliases
                    .Where(a => a.IPAddress != null && a.IPAddress == ip)
                    .SelectMany(a => a.Link.ReceivedPenalties)
                    .Where(filter);

                var activePenalties = (await iqLinkPenalties.ToListAsync())
                    .Union(await iqIPPenalties.ToListAsync())
                    .Distinct();

                // this is a bit more performant in memory (ordering)
                return activePenalties.OrderByDescending(p => p.When).ToList();
            }
        }

        public virtual async Task RemoveActivePenalties(int aliasLinkId)
        {
            using (var context = new DatabaseContext())
            {
                var now = DateTime.UtcNow;
                await context.Penalties
                    .Where(p => p.LinkId == aliasLinkId)
                    .Where(p => p.Expires > now || p.Expires == null)
                    .ForEachAsync(p =>
                    {
                        p.Active = false;
                        p.Expires = now;
                    });

                await context.SaveChangesAsync();
            }
        }
    }
}
