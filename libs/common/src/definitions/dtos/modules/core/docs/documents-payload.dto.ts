import { documentDtoFactory } from '@hsm-lib/common/utils/';
import { Type } from 'class-transformer';
import { IsArray, IsNotEmpty, IsString, ValidateNested } from 'class-validator';

class FileInfoDto {
  @IsNotEmpty()
  @IsString()
  fileId: string;
}

class DocumentDto extends documentDtoFactory(FileInfoDto) {}

export class DocumentsPayloadDto {
  @IsArray()
  @ValidateNested({ each: true })
  @Type(() => DocumentDto)
  documents: DocumentDto[];
}
