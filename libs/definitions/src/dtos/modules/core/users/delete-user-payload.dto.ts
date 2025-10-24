import { IsNotEmpty, IsString } from 'class-validator';

export class DeleteUserPayloadDto {
  @IsNotEmpty()
  @IsString()
  id!: string;
}
