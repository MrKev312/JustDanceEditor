# Errors
Just Dance Next is very prone to errors, here are some common errors and how to fix them.

## Infinite Loading Screen
If loading a map keeps loading forever, delete the song's cache folder and recopy the song.

## White Video
If the video is white, the video file is corrupted, delete the video file and recopy it.

## Panda-0008_0101 Error
``Panda-0008_0101`` means that it can't find the cache folder at all for the song,
make sure that the song is in the correct location and that it copied correctly,
DBI sometimes has issues with copying files, so make sure that the files are there.

### Speculation
This can also happen when the cache the song is in is higher or equal to ``SD_Cache.002A``,
this is because the cache is too high and the game can't find it,
to fix this, make sure there are fewer caches.
If all caches are already at max size and can't get bigger, you can try to switch over to ``exFAT``, which can have bigger cache sizes.
Do note that ``exFAT`` is more error-prone than ``FAT32`` and can corrupt files more easily and can corrupt the _**whole**_ drive.

``Do this at your own risk as I'm not responsible for any data loss.``

## Jackrose-0102_0007 Error
``Jackrose-0102_0007`` means that the song is missing some files,
make sure that the song has all the required files anfd that they were copied correctly,
again, DBI sometimes has issues with copying files, so make sure that the files are there.