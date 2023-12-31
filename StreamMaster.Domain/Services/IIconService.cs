﻿using StreamMaster.Domain.Dto;

namespace StreamMaster.Domain.Services
{
    public interface IIconService
    {

        void AddIcon(IconFileDto iconFile);
        void AddIcons(List<IconFileDto> newIconFiles);
        void ClearIcons();
        void ClearTvLogos();
        IconFileDto? GetIconBySource(string source);
        List<IconFileDto> GetIcons(SMFileTypes? SMFileType = null);
        Task<bool> ReadDirectoryTVLogos(CancellationToken cancellationToken = default);
        void RemoveIconsByM3UFileId(int id);
        //void SetIndexes();
    }
}