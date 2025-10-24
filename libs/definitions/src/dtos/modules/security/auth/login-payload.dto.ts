import { IsNotEmpty, IsString } from 'class-validator';

export class LoginPayloadDto {
  @IsNotEmpty()
  @IsString()
  username: string;

  @IsNotEmpty()
  @IsString()
  password: string;
}
