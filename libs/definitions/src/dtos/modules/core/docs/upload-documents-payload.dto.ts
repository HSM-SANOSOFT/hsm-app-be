import { plainToInstance, Transform, Type } from 'class-transformer';
import { IsArray, IsNotEmpty, IsString, ValidateNested } from 'class-validator';

export class FilesDto {
  @IsNotEmpty()
  @IsString()
  filename: string;

  @IsNotEmpty()
  @IsString()
  foldername: string;
}

export class UploadDocumentPayload {
  @IsNotEmpty()
  @IsString()
  bucket: string;

  @IsArray()
  @ValidateNested({ each: true })
  @Type(() => FilesDto)
  files: FilesDto[];
}

export class UploadDocumentPayloadDto {
  @Transform(
    ({ value }) => {
      const parsed = typeof value === 'string' ? JSON.parse(value) : value;
      return plainToInstance(UploadDocumentPayload, parsed);
    },
    { toClassOnly: true },
  )
  @IsArray()
  @ValidateNested({ each: true })
  @Type(() => UploadDocumentPayload)
  payload: UploadDocumentPayload[];
}
