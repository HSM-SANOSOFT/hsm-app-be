import { IsNotEmpty, IsString } from 'class-validator';

export class LogoutPayloadDto {
  @IsNotEmpty()
  @IsString()
  id: string;
}
