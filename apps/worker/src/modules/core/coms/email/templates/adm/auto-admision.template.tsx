import { AutoAdmisionTemplateDto } from '@hsm-lib/definitions/dtos';
export function AutoAdmisionTemplate(
  data: AutoAdmisionTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>Estimad@ paciente: {data.patientName}</p>
      <p>Le saludamos del Hospital Santamaría.</p>
      <p>
        Hemos registrado a su historia clínica #{data.documentId}, un acceso con
        fecha {data.date}.
      </p>
      <p>
        Que contiene un código de verificación necesario identificarse en
        nuestro sistema.{' '}
      </p>
      <p>Código de verificación: {data.code}</p>
      <p>Gracias por su colaboración.</p>
      <p>Atentamente,</p>
      <p>Admisiones</p>
      <p>Servicio Ambulatorio</p>
      <p>Celular 0981764904</p>
    </>
  );
}
