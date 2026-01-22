import { Type } from 'class-transformer';
import { IsArray, IsNotEmpty, IsString, ValidateNested } from 'class-validator';

export class UploadFileDataDto {
  @IsNotEmpty()
  @IsString()
  filename: string;

  @IsNotEmpty()
  @IsString()
  foldername: string;

  @IsNotEmpty()
  data: Buffer;

  @IsNotEmpty()
  @IsString()
  contentType: string;
}

export class UploadBucketFilesDto {
  @IsNotEmpty()
  @IsString()
  bucket: string;

  @IsArray()
  @ValidateNested({ each: true })
  @Type(() => UploadFileDataDto)
  files: UploadFileDataDto[];
}

export class UploadToS3Dto {
  @IsArray()
  @ValidateNested({ each: true })
  @Type(() => UploadBucketFilesDto)
  payload: UploadBucketFilesDto[];
}
