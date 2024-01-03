﻿namespace StreamMaster.Domain.Extensions;

public static class ImageExtensions
{
    public static string GetSDImageFullPath(this string fileName)
    {
        string subdirectoryName = fileName[0].ToString().ToLower();
        string logoPath = Path.Combine(BuildInfo.SDImagesFolder, subdirectoryName, fileName);

        return logoPath;
    }
}