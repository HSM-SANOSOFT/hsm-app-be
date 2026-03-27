import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import { IsNotEmpty, IsString } from 'class-validator';

@ApiSchema({ name: 'Login Payload' })
export class LoginPayloadDto {
  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  username: string;

  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  password: string;
}
