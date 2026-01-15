import {
  FacturaGeneradaTemplateDto,
  FacturaListaTemplateDto,
} from '@hsm-lib/definitions/dtos';
import { FacEmailTemplate } from '@hsm-lib/definitions/enums';

export type FacTemplateDtoMap = {
  [FacEmailTemplate.FacturaGenerada]: FacturaGeneradaTemplateDto;
  [FacEmailTemplate.FacturaLista]: FacturaListaTemplateDto;
};
