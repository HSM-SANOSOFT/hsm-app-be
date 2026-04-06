import { Type } from 'class-transformer';
import {
  IsArray,
  IsNotEmpty,
  IsObject,
  IsString,
  ValidateNested,
} from 'class-validator';

type Ctor<T> = new () => T;

function fileDtoFactory<TFileInfo>(FileInfoClass: Ctor<TFileInfo>) {
  class FileDto {
    @IsNotEmpty()
    @IsString()
    folderName: string;

    @IsNotEmpty()
    @IsObject()
    @ValidateNested()
    @Type(() => FileInfoClass)
    fileInfo: TFileInfo;
  }
  return FileDto;
}

export function documentDtoFactory<TFileInfo>(FileInfoClass: Ctor<TFileInfo>) {
  const FileDto = fileDtoFactory(FileInfoClass);
  class DocumentDto {
    @IsNotEmpty()
    @IsString()
    bucket: string;

    @IsArray()
    @ValidateNested({ each: true })
    @Type(() => FileDto)
    files: InstanceType<typeof FileDto>[];
  }
  return DocumentDto;
}
