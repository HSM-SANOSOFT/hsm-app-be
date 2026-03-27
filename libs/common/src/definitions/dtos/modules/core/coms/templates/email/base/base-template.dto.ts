import { IsNotEmpty, IsString } from 'class-validator';

export class BaseTemplateDto {
  @IsNotEmpty()
  @IsString()
  title: string;

  @IsNotEmpty()
  body: React.ReactNode;

  @IsNotEmpty()
  @IsString()
  currentYear: number;
}
