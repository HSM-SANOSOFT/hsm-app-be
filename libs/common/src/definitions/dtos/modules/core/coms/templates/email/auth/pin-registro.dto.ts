import { IsNotEmpty, IsNumber, IsString } from 'class-validator';

export class PinRegistroTemplateDto {
  @IsNotEmpty()
  @IsString()
  date: string;

  @IsNotEmpty()
  @IsNumber()
  pin: number;
}
