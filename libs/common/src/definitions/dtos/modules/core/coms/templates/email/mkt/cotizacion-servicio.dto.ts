import { IsNotEmpty, IsString } from 'class-validator';

export class CotizacionServicioTemplateDto {
  @IsString()
  @IsNotEmpty()
  serviceName: string;
}
