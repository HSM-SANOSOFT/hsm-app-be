import {
  CampanaPromocionalTemplateDto,
  CotizacionServicioTemplateDto,
} from '@hsm-lib/definitions/dtos';
import { MktEmailTemplate } from '@hsm-lib/definitions/enums';

export type MktTemplateDtoMap = {
  [MktEmailTemplate.CampanaPromocional]: CampanaPromocionalTemplateDto;
  [MktEmailTemplate.CotizacionServicio]: CotizacionServicioTemplateDto;
};
