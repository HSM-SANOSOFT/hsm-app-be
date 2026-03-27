import { documentDtoFactory } from '@hsm-lib/common/utils/';
import { plainToInstance, Transform, Type } from 'class-transformer';
import { IsArray, IsNotEmpty, IsString, ValidateNested } from 'class-validator';

class FileInfoDto {
  @IsNotEmpty()
  @IsString()
  fileName: string;
}

class UploadDocument extends documentDtoFactory(FileInfoDto) {}

export class UploadDocumentPayloadDto {
  @Transform(
    ({ value }) => {
      const parsed = typeof value === 'string' ? JSON.parse(value) : value;
      return plainToInstance(UploadDocument, parsed);
    },
    { toClassOnly: true },
  )
  @IsArray()
  @ValidateNested({ each: true })
  @Type(() => UploadDocument)
  payload: UploadDocument[];
}
