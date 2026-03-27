import { documentDtoFactory } from '@hsm-lib/common/utils/';
import { Type } from 'class-transformer';
import { IsArray, IsNotEmpty, IsString, ValidateNested } from 'class-validator';

class S3FileInfoUploadDto {
  @IsNotEmpty()
  @IsString()
  fileName: string;

  @IsNotEmpty()
  fileBuffer: Buffer;

  @IsNotEmpty()
  @IsString()
  contentType: string;
}

export class S3FileUploadDto extends documentDtoFactory(S3FileInfoUploadDto) {}

export class S3FileUploadPayloadDto {
  @IsArray()
  @ValidateNested({ each: true })
  @Type(() => S3FileUploadDto)
  payload: S3FileUploadDto[];
}
